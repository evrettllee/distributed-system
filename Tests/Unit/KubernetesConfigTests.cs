using Microsoft.Extensions.Configuration;
using Server.Startup;
using Xunit;

namespace Tests.Unit
{
    public class KubernetesConfigTests
    {
        // Тест 61 — APP_MODE: значение не задано — используется default, задано — используется оно
        [Theory]
        [InlineData(null, "default")]
        [InlineData("", "default")]
        [InlineData("   ", "default")]
        [InlineData("kubernetes", "kubernetes")]
        [InlineData(" kubernetes ", "kubernetes")]
        public void Settings_AppMode_DefaultOrConfigured(string? value, string expected)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["APP_MODE"] = value })
                .Build();

            Assert.Equal(expected, ServerSettings.ReadMode(configuration));
        }

        // Тест 62 — APP_MODE читается из переменной окружения 
        [Fact]
        public void Settings_AppMode_ReadFromEnvironmentVariable()
        {
            const string variable = "APP_MODE";

            Environment.SetEnvironmentVariable(variable, "kubernetes");
            try
            {
                IConfiguration configuration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();

                Assert.Equal("docker", ServerSettings.ReadMode(configuration));
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        }

        // Тест 63 — APP_VERSION: не задана — используется dev, задана — номер сборки
        [Theory]
        [InlineData(null, "dev")]
        [InlineData("", "dev")]
        [InlineData("12", "12")]
        [InlineData(" 12 ", "12")]
        public void Settings_AppVersion_DefaultOrConfigured(string? value, string expected)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["APP_VERSION"] = value })
                .Build();

            Assert.Equal(expected, ServerSettings.ReadVersion(configuration));
        }
    }
}