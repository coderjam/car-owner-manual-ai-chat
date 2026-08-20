using CarManualAssistant.Api.Models;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace CarManualAssistant.Api.Services;

public sealed class AdminAuthService
{
    private readonly AdminOptions _options;

    public AdminAuthService(IOptions<AdminOptions> options)
    {
        _options = options.Value;
    }

    public AdminLoginResponse? Login(AdminLoginRequest request)
    {
        // V1 先使用配置文件里的后台账号密码。
        // 这样管理端和用户端已经完成权限隔离；后续正式上线时，
        // 这里可以替换为数据库管理员表、密码哈希、JWT 和登录失败次数限制。
        var usernameMatched = FixedEquals(request.Username, _options.Username);
        var passwordMatched = FixedEquals(request.Password, _options.Password);

        if (!usernameMatched || !passwordMatched)
        {
            return null;
        }

        return new AdminLoginResponse(
            Username: _options.Username,
            Token: _options.Token);
    }

    public bool IsAuthorized(string authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        var expected = $"Bearer {_options.Token}";
        return FixedEquals(authorizationHeader, expected);
    }

    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
