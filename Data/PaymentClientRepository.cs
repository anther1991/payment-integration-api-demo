using System.Data;
using Microsoft.Data.SqlClient;
using API_Thanh_toan.Models;
using Dapper;

namespace API_Thanh_toan.Data;

public class PaymentClientRepository : IPaymentClientRepository
{
    private readonly string _connectionString;

    public PaymentClientRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<PaymentClient?> GetByIdAsync(string clientId)
    {
        const string sql = @"
            SELECT ClientId, ClientSecretSalt, ClientSecretHash, IsActive, MANH AS Manh, payID AS PayId, ClientSecretEncrypted, CreatedAt, UpdatedAt
            FROM PaymentClients
            WHERE ClientId = @ClientId";

        using var connection = CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PaymentClient>(sql, new { ClientId = clientId });
    }
}
