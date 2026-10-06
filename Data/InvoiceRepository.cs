using System.Data;
using Microsoft.Data.SqlClient;
using API_Thanh_toan.Models;
using Dapper;

namespace API_Thanh_toan.Data;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly string _connectionString;

    public InvoiceRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<Customer?> GetCustomerByIdAsync(int customerCode)
    {
        const string sql = @"
            SELECT IDKH AS Idkh, TENKH AS Tenkh, SODT AS Sodt, TTSD AS Ttsd, GCDB_KDPT AS GcdbKdpt
            FROM KHACHHANG
            WHERE IDKH = @CustomerCode";

        using var connection = CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Customer>(sql, new { CustomerCode = customerCode });
    }

    public async Task<IEnumerable<InvoiceDto>> GetUnpaidInvoicesByCustomerIdAsync(int customerCode)
    {
        const string sql = @"
            SELECT 
                hddt.IDHD          AS InvoiceCode,
                hddt.MAUSO          AS InvoiceForm,
                hddt.KYHIEU          AS InvoiceSymbol,
                hddt.SOSERIAL        AS InvoiceSerial,
                tt.NAM               AS InvoiceYear,
                tt.THANG             AS InvoiceMonth,
                tt.DIACHI            AS Address,
                tt.TONGCONG          AS AmountDue,
                hddt.NGAYSERIAL       AS IssuedDate
            FROM TIEUTHU tt
            INNER JOIN TIEUTHU_HDDT hddt 
                ON tt.IDKH = hddt.IDKH 
               AND tt.NAM = hddt.NAM 
               AND tt.THANG = hddt.THANG
            WHERE tt.IDKH = @CustomerCode
              AND tt.CONNO = 1
              AND tt.cashMANH IS NULL
              AND hddt.eSTATE IN ('DONE', 'CASTED')
            ORDER BY tt.NAM, tt.THANG";

        using var connection = CreateConnection();
        return await connection.QueryAsync<InvoiceDto>(sql, new { CustomerCode = customerCode });
    }
}
