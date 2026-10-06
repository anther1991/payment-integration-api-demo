# OAuth2 Client Credentials API (Token Endpoint) - .NET 8

Dự án này triển khai Endpoint xác thực Token Endpoint `/integration/v1/oauth/token` theo mô hình **OAuth2 Client Credentials (RFC 6749)** sử dụng **C# (.NET 8)**, **Dapper** và **SQL Server**.

## 1. Cấu trúc Database (PaymentClients)

Bảng này đã được thiết kế sẵn và cần được tạo thủ công trong SQL Server (ví dụ Database: `PaymentDemo`):

```sql
CREATE TABLE PaymentClients (
    ClientId NVARCHAR(100) NOT NULL PRIMARY KEY,
    ClientSecretSalt NVARCHAR(64) NOT NULL,   -- Chuỗi muối ngẫu nhiên sinh khi tạo Client
    ClientSecretHash NVARCHAR(64) NOT NULL,   -- SHA256(Secret + Salt), dạng Hex, 64 ký tự
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
```

## 2. Dữ liệu thử nghiệm (Seeding data)

Tạo một client thử nghiệm với secret của riêng bạn (không dùng secret thật của đối tác).
Sinh `Salt` và `Hash` bằng đoạn mã bên dưới rồi chèn vào bảng:

```sql
INSERT INTO PaymentClients (ClientId, ClientSecretSalt, ClientSecretHash, IsActive, CreatedAt, UpdatedAt)
VALUES ('demo_client', '<salt-hex>', '<hash-hex>', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
```

### Cách tự sinh Salt & Hash cho các Client mới (C# Snippet)
Để sinh mật khẩu và băm an toàn khi tạo thêm đối tác:
```csharp
using System.Security.Cryptography;
using System.Text;

// 1. Sinh Salt ngẫu nhiên
byte[] saltBytes = RandomNumberGenerator.GetBytes(32);
string saltHex = Convert.ToHexString(saltBytes).ToLower();

// 2. Hash mật khẩu với Salt
string plaintextSecret = "mật_khẩu_đối_tác_sinh_ra";
byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextSecret + saltHex));
string hashHex = Convert.ToHexString(hashBytes).ToLower();

Console.WriteLine($"Salt: {saltHex}");
Console.WriteLine($"Hash: {hashHex}");
```

## 3. Hướng dẫn chạy và kiểm thử (Testing)

### Khởi chạy dự án
Di chuyển vào thư mục dự án và chạy:
```bash
dotnet run
```

### Sử dụng PowerShell để gọi API
Bạn có thể dùng lệnh `Invoke-RestMethod` trong PowerShell để gửi yêu cầu lấy Token:

```powershell
# Header Authorization: Basic <base64(client_id:client_secret)>
# (Base64 của "client_id:client_secret")

$headers = @{
    "Authorization" = "Basic <base64(client_id:client_secret)>"
}

$body = @{
    "grant_type" = "client_credentials"
}

$response = Invoke-RestMethod -Uri "https://localhost:7271/integration/v1/oauth/token" -Method Post -Headers $headers -Body $body -ContentType "application/x-www-form-urlencoded"
$response | ConvertTo-Json
```
*(Lưu ý: Hãy thay đổi cổng `7271` bằng cổng HTTPS thực tế được cấp khi bạn chạy `dotnet run`)*

### Sử dụng cURL
```bash
curl -X POST https://localhost:7271/integration/v1/oauth/token \
  -H "Authorization: Basic <base64(client_id:client_secret)>" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials"
```

## 4. Chạy Unit Tests
Để đảm bảo tất cả các logic bảo mật chạy đúng đắn (bao gồm Timing-attack protection, Rate limiting, Inactive clients):
```bash
dotnet test ..\API_Thanh_toan.Tests\API_Thanh_toan.Tests.csproj
```
