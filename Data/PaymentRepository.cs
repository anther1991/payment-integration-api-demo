using System.Data;
using API_Thanh_toan.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace API_Thanh_toan.Data;

public class PaymentRepository : IPaymentRepository
{
    private readonly string _connectionString;

    public PaymentRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<PaymentClient?> GetClientAuthDetailsAsync(string clientId)
    {
        const string sql = @"
            SELECT ClientId, MANH AS Manh, ClientSecretEncrypted, IsActive, payID AS PayId
            FROM PaymentClients
            WHERE ClientId = @ClientId";

        using var connection = CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PaymentClient>(sql, new { ClientId = clientId });
    }

    public async Task<(long Id, string PhaseAResult, PaymentTransactionRecord? ExistingRecord)> InsertProcessingTransactionAsync(
        string clientId, 
        string partnerTxId, 
        string? gatewayTxId, 
        int customerCode, 
        long totalAmount, 
        DateTime paymentTime, 
        string? paymentProvider, 
        string? paymentChannel)
    {
        const string insertSql = @"
            INSERT INTO PaymentTransactions (
                ClientId, PartnerTransactionId, GatewayTransactionId, CustomerCode, 
                TotalAmount, PaymentTime, PaymentProvider, PaymentChannel, Status, ProcessedAt
            )
            OUTPUT INSERTED.Id
            VALUES (
                @ClientId, @PartnerTxId, @GatewayTxId, @CustomerCode, 
                @TotalAmount, @PaymentTime, @PaymentProvider, @PaymentChannel, 'PROCESSING', SYSUTCDATETIME()
            );";

        using var connection = CreateConnection();
        connection.Open();

        try
        {
            var id = await connection.ExecuteScalarAsync<long>(insertSql, new
            {
                ClientId = clientId,
                PartnerTxId = partnerTxId,
                GatewayTxId = gatewayTxId,
                CustomerCode = customerCode,
                TotalAmount = totalAmount,
                PaymentTime = paymentTime,
                PaymentProvider = paymentProvider,
                PaymentChannel = paymentChannel
            });

            return (id, "NEW", null);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601) // Unique constraint violation (UQ_PartnerTransactionId)
        {
            // Unique violation -> Query existing transaction record
            const string selectSql = @"
                SELECT Id, ClientId, PartnerTransactionId, Status, ErrorCode, ErrorMessage, ProcessedAt
                FROM PaymentTransactions
                WHERE ClientId = @ClientId AND PartnerTransactionId = @PartnerTxId";

            var existing = await connection.QuerySingleOrDefaultAsync<PaymentTransactionRecord>(selectSql, new
            {
                ClientId = clientId,
                PartnerTxId = partnerTxId
            });

            if (existing == null)
            {
                throw;
            }

            if (existing.Status == "SUCCESS" || existing.Status == "FAILED")
            {
                return (existing.Id, "ALREADY_COMPLETED", existing);
            }

            // Status is PROCESSING -> Check freshness threshold (5 seconds)
            var age = DateTime.UtcNow - existing.ProcessedAt;
            if (age <= TimeSpan.FromSeconds(5))
            {
                return (existing.Id, "PROCESSING_IN_PROGRESS", existing);
            }

            // Older than 5s -> Stale processing attempt, resume Phase B using existing record Id
            return (existing.Id, "STALE_PROCESSING", existing);
        }
    }

    public async Task UpdateTransactionStatusAsync(long transactionId, string status, string? errorCode, string? errorMessage)
    {
        const string sql = @"
            UPDATE PaymentTransactions 
            SET Status = @Status, ErrorCode = @ErrorCode, ErrorMessage = @ErrorMessage
            WHERE Id = @TransactionId";

        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, new
        {
            TransactionId = transactionId,
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        });
    }

    public async Task<(bool Success, string? ErrorCode, string? ErrorMessage)> ExecutePaymentPhaseBAsync(
        long transactionId, 
        int payId,
        string clientManh, 
        int customerCode, 
        string? payerAccountHolder, 
        string? payerAccountNumber, 
        List<PaymentInvoiceItemDto> invoices)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Calculate local Vietnam execution time (UTC+7)
            var ocDate = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "SE Asia Standard Time");

            // Step 5: Check each invoice in the list first
            var validatedItems = new List<(PaymentInvoiceItemDto Item, int Nam, int Thang)>();

            foreach (var item in invoices)
            {
                if (!item.InvoiceCode.HasValue || !item.AmountApplied.HasValue)
                {
                    transaction.Rollback();
                    return (false, "invalid_request", "Missing invoice parameters.");
                }

                const string checkSql = @"
                    SELECT hddt.NAM AS Nam, hddt.THANG AS Thang, tt.TONGCONG AS Tongcong, tt.CONNO AS Conno, tt.cashMANH AS CashManh, hddt.eSTATE AS EState
                    FROM TIEUTHU_HDDT hddt
                    INNER JOIN TIEUTHU tt 
                        ON tt.IDKH = hddt.IDKH AND tt.NAM = hddt.NAM AND tt.THANG = hddt.THANG
                    WHERE hddt.IDHD = @InvoiceCode AND hddt.IDKH = @CustomerCode";

                var invState = await connection.QuerySingleOrDefaultAsync(checkSql, new
                {
                    InvoiceCode = item.InvoiceCode.Value,
                    CustomerCode = customerCode
                }, transaction);

                if (invState == null)
                {
                    transaction.Rollback();
                    return (false, "invoice_not_found", $"Hóa đơn {item.InvoiceCode} không tồn tại hoặc không thuộc về khách hàng {customerCode}.");
                }

                if (!((bool)invState.Conno) || invState.CashManh != null)
                {
                    transaction.Rollback();
                    return (false, "already_paid", $"Hóa đơn {item.InvoiceCode} đã được ghi nhận thanh toán trước đó.");
                }

                string eState = (string)invState.EState;
                if (eState != "DONE" && eState != "CASTED")
                {
                    transaction.Rollback();
                    return (false, "invoice_not_issued", $"Hóa đơn {item.InvoiceCode} chưa được phát hành chính thức.");
                }

                long dbAmount = Convert.ToInt64(invState.Tongcong);
                if (item.AmountApplied.Value != dbAmount)
                {
                    transaction.Rollback();
                    return (false, "amount_mismatch", $"Số tiền thanh toán ({item.AmountApplied.Value}) không khớp với số tiền của hóa đơn {item.InvoiceCode} trong CSDL ({dbAmount}).");
                }

                validatedItems.Add((item, (int)invState.Nam, (int)invState.Thang));
            }

            // Step 7: Apply Updates & Inserts for each invoice
            foreach (var (item, nam, thang) in validatedItems)
            {
                const string updateSql = @"
                    UPDATE TIEUTHU
                    SET cashMANH = @ClientManh,
                        cashCHUTK = @PayerAccountHolder,
                        cashSOTK = @PayerAccountNumber
                    WHERE IDKH = @CustomerCode AND NAM = @Nam AND THANG = @Thang
                      AND CONNO = 1 AND cashMANH IS NULL";

                var rowsAffected = await connection.ExecuteAsync(updateSql, new
                {
                    ClientManh = clientManh,
                    PayerAccountHolder = payerAccountHolder,
                    PayerAccountNumber = payerAccountNumber,
                    CustomerCode = customerCode,
                    Nam = nam,
                    Thang = thang
                }, transaction);

                if (rowsAffected != 1)
                {
                    transaction.Rollback();
                    return (false, "concurrent_update_conflict", $"Xung đột cập nhật đồng thời cho hóa đơn {item.InvoiceCode}.");
                }

                const string insertInvoiceSql = @"
                    INSERT INTO PaymentTransactionInvoices (TransactionId, InvoiceCode, IDKH, NAM, THANG, AmountApplied)
                    VALUES (@TransactionId, @InvoiceCode, @CustomerCode, @Nam, @Thang, @AmountApplied)";

                await connection.ExecuteAsync(insertInvoiceSql, new
                {
                    TransactionId = transactionId,
                    InvoiceCode = item.InvoiceCode!.Value,
                    CustomerCode = customerCode,
                    Nam = nam,
                    Thang = thang,
                    AmountApplied = item.AmountApplied!.Value
                }, transaction);

                const string insertOnlCashedSql = @"
                    INSERT INTO onlCASHED (MANH, payID, ocDate, NAM, THANG, IDKH, CHUTK, SOTK, isApproved)
                    VALUES (@ClientManh, @PayId, @OcDate, @Nam, @Thang, @CustomerCode, @PayerAccountHolder, @PayerAccountNumber, 0)";

                await connection.ExecuteAsync(insertOnlCashedSql, new
                {
                    ClientManh = clientManh,
                    PayId = payId,
                    OcDate = ocDate,
                    Nam = nam,
                    Thang = thang,
                    CustomerCode = customerCode,
                    PayerAccountHolder = payerAccountHolder,
                    PayerAccountNumber = payerAccountNumber
                }, transaction);
            }

            // Everything succeeded -> Commit Phase B transaction
            transaction.Commit();
            return (true, null, null);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return (false, "internal_error", ex.Message);
        }
    }
}
