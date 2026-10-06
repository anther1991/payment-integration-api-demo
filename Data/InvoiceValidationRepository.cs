using System.Data;
using Microsoft.Data.SqlClient;
using API_Thanh_toan.Models;
using Dapper;

namespace API_Thanh_toan.Data;

public class InvoiceValidationRepository : IInvoiceValidationRepository
{
    private readonly string _connectionString;

    public InvoiceValidationRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<InvoiceValidationResult?> ValidateInvoiceCrossCheckAsync(int customerCode, int invoiceCode)
    {
        const string sql = @"
            SELECT 
                kh.TTSD          AS Ttsd, 
                kh.GCDB_KDPT     AS GcdbKdpt,
                tt.CONNO         AS Conno, 
                tt.cashMANH      AS CashManh, 
                tt.TONGCONG      AS Tongcong,
                hddt.eSTATE      AS EState
            FROM TIEUTHU_HDDT hddt
            INNER JOIN TIEUTHU tt 
                ON tt.IDKH = hddt.IDKH AND tt.NAM = hddt.NAM AND tt.THANG = hddt.THANG
            INNER JOIN KHACHHANG kh 
                ON kh.IDKH = hddt.IDKH
            WHERE hddt.IDHD = @InvoiceCode
              AND hddt.IDKH = @CustomerCode";

        using var connection = CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<InvoiceValidationResult>(sql, new 
        { 
            InvoiceCode = invoiceCode, 
            CustomerCode = customerCode 
        });
    }
}
