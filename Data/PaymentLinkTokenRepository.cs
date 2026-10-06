using System.Data;
using API_Thanh_toan.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace API_Thanh_toan.Data;

public class PaymentLinkTokenRepository : IPaymentLinkTokenRepository
{
    private readonly string _connectionString;

    public PaymentLinkTokenRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<PaymentLinkToken?> GetTokenRecordAsync(string token)
    {
        const string sql = @"
            SELECT Token, CustomerCode, ExpiresAt, CreatedAt
            FROM PaymentLinkTokens
            WHERE Token = @Token";

        using var connection = CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PaymentLinkToken>(sql, new { Token = token });
    }
}
