using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using Microsoft.Extensions.Caching.Memory;

namespace API_Thanh_toan.Services;

public class InvoiceLookupService : IInvoiceLookupService
{
    private readonly IInvoiceRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InvoiceLookupService> _logger;
    private readonly int _sequentialThreshold;
    private readonly int _rateLimitThreshold;

    public InvoiceLookupService(
        IInvoiceRepository repository, 
        IMemoryCache cache, 
        IConfiguration configuration,
        ILogger<InvoiceLookupService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
        
        _sequentialThreshold = int.TryParse(configuration["SecuritySettings:SequentialThreshold"], out var seq) ? seq : 5;
        _rateLimitThreshold = int.TryParse(configuration["SecuritySettings:InvoiceLookupRateLimit"], out var val) ? val : 30;
    }

    public bool IsRateLimited(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        // Fixed-window rate limiting per minute
        var currentMinute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"rate-limit:lookup:{clientId.ToLowerInvariant()}:{currentMinute}";

        if (_cache.TryGetValue<int>(key, out var count))
        {
            if (count >= _rateLimitThreshold)
            {
                return true;
            }
            _cache.Set(key, count + 1, TimeSpan.FromMinutes(2));
        }
        else
        {
            _cache.Set(key, 1, TimeSpan.FromMinutes(2));
        }

        return false;
    }

    public void DetectSequentialQueryPattern(string clientId, int customerCode, string sourceIp)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return;

        var key = $"seq-detection:{clientId.ToLowerInvariant()}";
        List<(int customerCode, DateTime timestamp)>? history;

        if (!_cache.TryGetValue(key, out history) || history == null)
        {
            history = new List<(int customerCode, DateTime timestamp)>();
        }

        var now = DateTime.UtcNow;
        history.Add((customerCode, now));

        // 1. Filter out items older than 1 minute
        history = history.Where(x => now - x.timestamp <= TimeSpan.FromMinutes(1)).ToList();

        // 2. Deduplicate customer codes
        var uniqueCodes = history.Select(x => x.customerCode).Distinct().ToList();

        // 3. Sort ascending
        uniqueCodes.Sort();

        // 4. Find the longest consecutive sequence
        int maxSeqLength = 0;
        int currentSeqLength = 0;
        var currentSeq = new List<int>();
        var longestSeq = new List<int>();

        if (uniqueCodes.Count > 0)
        {
            currentSeq.Add(uniqueCodes[0]);
            currentSeqLength = 1;
            maxSeqLength = 1;
            longestSeq = new List<int>(currentSeq);

            for (int i = 1; i < uniqueCodes.Count; i++)
            {
                if (uniqueCodes[i] == uniqueCodes[i - 1] + 1)
                {
                    currentSeqLength++;
                    currentSeq.Add(uniqueCodes[i]);
                }
                else
                {
                    if (currentSeqLength > maxSeqLength)
                    {
                        maxSeqLength = currentSeqLength;
                        longestSeq = new List<int>(currentSeq);
                    }
                    currentSeqLength = 1;
                    currentSeq.Clear();
                    currentSeq.Add(uniqueCodes[i]);
                }
            }

            if (currentSeqLength > maxSeqLength)
            {
                maxSeqLength = currentSeqLength;
                longestSeq = new List<int>(currentSeq);
            }
        }

        // 5. Trigger warning log if threshold is reached
        if (maxSeqLength >= _sequentialThreshold)
        {
            var seqString = string.Join(", ", longestSeq);
            _logger.LogWarning(
                "Sequential lookup pattern detected! Client ID: '{ClientId}' from IP: '{SourceIp}'. Sequence: [{Sequence}]",
                clientId, sourceIp, seqString);
        }

        // Save updated history
        _cache.Set(key, history, TimeSpan.FromMinutes(2));
    }

    public async Task<CustomerInvoicesResponse?> LookupInvoicesAsync(int customerCode)
    {
        // Step 1: Check if customer exists and is eligible for collection
        var customer = await _repository.GetCustomerByIdAsync(customerCode);
        if (customer == null)
        {
            return null; // Not found -> 404
        }

        var isEligible = customer.Ttsd != "CUP" && !customer.GcdbKdpt;

        var response = new CustomerInvoicesResponse
        {
            CustomerCode = customer.Idkh,
            CustomerName = customer.Tenkh ?? string.Empty,
            PhoneNumber = customer.Sodt ?? string.Empty,
            EligibleForCollection = isEligible,
            TotalAmountDue = 0,
            Invoices = new List<InvoiceDto>()
        };

        if (!isEligible)
        {
            return response; // Return empty list immediately without querying invoices
        }

        // Step 2: Fetch unpaid invoices
        var invoices = (await _repository.GetUnpaidInvoicesByCustomerIdAsync(customerCode)).ToList();
        
        foreach (var invoice in invoices)
        {
            // Format period as MM/YYYY
            invoice.InvoicePeriod = $"{invoice.InvoiceMonth:D2}/{invoice.InvoiceYear}";

            // Represent explicitly as Vietnam Time (+07:00)
            if (invoice.IssuedDate.HasValue)
            {
                var rawDateTime = invoice.IssuedDate.Value.DateTime;
                invoice.IssuedDate = new DateTimeOffset(rawDateTime, TimeSpan.FromHours(7));
            }
        }

        response.Invoices = invoices;
        response.TotalAmountDue = invoices.Sum(x => x.AmountDue);

        return response;
    }
}
