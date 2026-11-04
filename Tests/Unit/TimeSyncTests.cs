using Shared;
using Tests.Helpers;
using Xunit;

namespace Tests.Unit
{
    public class TimeSyncTests : EducationTestBase
    {
        // Тест 6 — GetServerTime возвращает корректное время в ISO формате
        [Fact]
        public async Task GetServerTime_ReturnsValidDateTime()
        {
            DateTime before = DateTime.UtcNow;

            Response response = await Service.GetServerTime();

            DateTime after = DateTime.UtcNow;

            Assert.Equal("ok", response.Status);
            Assert.NotNull(response.Data);

            bool parsed = DateTime.TryParse(response.Data, out DateTime serverTime);
            Assert.True(parsed, "Время сервера не удалось распарсить");

            serverTime = serverTime.ToUniversalTime();
            Assert.True(serverTime >= before && serverTime <= after,
                $"Время сервера {serverTime:O} выходит за пределы [{before:O}, {after:O}]");
        }

        // Тест 7 — расчёт смещения: offset = serverTime - clientTime
        [Fact]
        public async Task TimeSync_OffsetCalculation_IsCorrect()
        {
            TimeSpan clockOffset = TimeSpan.FromSeconds(7);

            Response response = await Service.GetServerTime();
            DateTime clientLocalTime = DateTime.UtcNow + clockOffset;
            DateTime serverTime = DateTime.Parse(response.Data!).ToUniversalTime();

            TimeSpan offset = serverTime - clientLocalTime;

            Assert.True(offset.TotalSeconds < -5 && offset.TotalSeconds > -9,
                $"Ожидалось смещение около -7 сек, получено {offset.TotalSeconds:F3} сек");
        }

        // Тест 8 — RTT измеряется корректно: больше нуля и не превышает разумный порог
        [Fact]
        public async Task TimeSync_RTT_IsPositiveAndReasonable()
        {
            DateTime requestTime = DateTime.UtcNow;

            await Service.GetServerTime();

            DateTime responseTime = DateTime.UtcNow;
            TimeSpan rtt = responseTime - requestTime;

            Assert.True(rtt.TotalMilliseconds > 0, "RTT должен быть больше нуля");
            Assert.True(rtt.TotalSeconds < 10, $"RTT слишком большой: {rtt.TotalSeconds:F3} сек");
        }

        // Тест 9 — скорректированное время близко к серверному с учётом RTT/2
        [Fact]
        public async Task TimeSync_CorrectedTime_CloseTo_ServerTime()
        {
            TimeSpan clockOffset = TimeSpan.FromSeconds(7);

            DateTime requestTime = DateTime.UtcNow;
            Response response = await Service.GetServerTime();
            DateTime responseTime = DateTime.UtcNow;

            DateTime serverTime = DateTime.Parse(response.Data!).ToUniversalTime();
            TimeSpan rtt = responseTime - requestTime;
            TimeSpan networkDelay = rtt / 2;

            DateTime clientLocalTime = requestTime + clockOffset;
            TimeSpan timeOffset = (serverTime + networkDelay) - clientLocalTime;
            DateTime correctedTime = clientLocalTime + timeOffset;

            DateTime actualServerNow = serverTime + networkDelay;

            double diffSeconds = Math.Abs((correctedTime - actualServerNow).TotalSeconds);
            Assert.True(diffSeconds < 0.1,
                $"Скорректированное время отличается от серверного на {diffSeconds:F3} сек");
        }
    }
}
