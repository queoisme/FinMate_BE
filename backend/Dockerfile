# ---------------------------------------------------------------- build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Directory.Build.props PHẢI có mặt trước restore: TargetFramework không nằm trong bất kỳ
# .csproj nào mà nằm ở file props dùng chung này. Thiếu nó thì restore hỏng theo kiểu khó truy.
COPY Directory.Build.props FinMate.sln ./
COPY FinMate.Domain/FinMate.Domain.csproj                 FinMate.Domain/
COPY FinMate.Application/FinMate.Application.csproj       FinMate.Application/
COPY FinMate.Infrastructure/FinMate.Infrastructure.csproj FinMate.Infrastructure/
COPY FinMate.API/FinMate.API.csproj                       FinMate.API/
COPY FinMate.Tests/FinMate.Tests.csproj                   FinMate.Tests/

# Restore đứng riêng một tầng: sửa code không làm mất cache của nó, mà restore mới là phần
# chậm nhất. Chỉ khi một .csproj đổi thì tầng này mới chạy lại.
RUN dotnet restore FinMate.sln

COPY . .

# Release chứ không phải Debug. Directory.Build.props bật TreatWarningsAsErrors nên bước này
# tự nó là một cổng thật: một warning là hỏng build, không lọt được vào image.
RUN dotnet publish FinMate.API -c Release -o /app --no-restore

# -------------------------------------------------------------- runtime stage
# aspnet chứ không phải sdk: ~200MB thay vì hơn 1GB, và không mang mã nguồn theo. Đủ dùng vì
# Directory.Build.props bật InvariantGlobalization nên không cần ICU.
#
# Không dùng bản `chiseled` dù nhỏ hơn nữa: nó không có shell, mà mất shell là mất luôn khả
# năng vào container xem xét khi có sự cố.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app .

# Chạy bằng user không đặc quyền. APP_UID (1654) do chính ảnh .NET 8+ định nghĩa sẵn.
# Kèm theo: Serilog chỉ ghi file ở Development, vì /app không cho user này ghi — và ghi log
# vào bên trong container thì cũng chẳng ai đọc được.
USER $APP_UID

# Không đặt ASPNETCORE_URLS: ảnh aspnet đã có ASPNETCORE_HTTP_PORTS=8080, và đặt cả hai thì
# Kestrel cảnh báo "Overriding HTTP_PORTS" ở mỗi lần khởi động.
EXPOSE 8080

# Chạy thẳng dll đã publish. `dotnet run` build lại mỗi lần container khởi động.
ENTRYPOINT ["dotnet", "FinMate.API.dll"]
