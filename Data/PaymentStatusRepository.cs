using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace API_Thanh_toan.Data;

public class PaymentStatusRepository : IPaymentStatusRepository
{
    private readonly string _connectionString;

    public PaymentStatusRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<PaymentTransactionRecord?> GetPaymentTransactionStatusAsync(string clientId, string partnerTransactionId)
    {
        const string sql = @"
            SELECT Id, ClientId, PartnerTransactionId, Status, ErrorCode, ErrorMessage, ProcessedAt
            FROM PaymentTransactions
            WHERE ClientId = @ClientId AND PartnerTransactionId = @PartnerTransactionId";

        using var connection = CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PaymentTransactionRecord>(sql, new
        {
            ClientId = clientId,
            PartnerTransactionId = partnerTransactionId
        });
    }
}
