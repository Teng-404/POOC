using Microsoft.Extensions.Caching.Memory;

namespace POOC.Services
{
    public class LoginRateLimiter
    {
        private readonly IMemoryCache _cache;
        private const int MaxAttempts = 5;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

        public LoginRateLimiter(IMemoryCache cache)
        {
            _cache = cache;
        }

        private static string Key(string ip) => $"login_attempts:{ip}";

        public bool IsBlocked(string ip)
        {
            _cache.TryGetValue(Key(ip), out int attempts);
            return attempts >= MaxAttempts;
        }

        public void RecordFailure(string ip)
        {
            var k = Key(ip);
            _cache.TryGetValue(k, out int attempts);
            _cache.Set(k, attempts + 1, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = Window
            });
        }

        public void Reset(string ip)
        {
            _cache.Remove(Key(ip));
        }
    }
}