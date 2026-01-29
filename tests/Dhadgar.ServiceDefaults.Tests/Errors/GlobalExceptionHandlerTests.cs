using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Dhadgar.ServiceDefaults.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Errors;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task DomainException_ReturnsItsStatusCode()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new NotFoundException("User", "123"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ArgumentException_Returns400()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new ArgumentException("Invalid argument"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task KeyNotFoundException_Returns404()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new KeyNotFoundException("Key not found"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OperationCanceledException_Returns499()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new OperationCanceledException("Client closed"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        ((int)response.StatusCode).Should().Be(499);
    }

    [Fact]
    public async Task GenericException_Returns500()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new InvalidOperationException("Unexpected error"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Response_IncludesTraceId()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new ValidationException("Validation failed"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("traceId", out var traceIdElement).Should().BeTrue();
        traceIdElement.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Response_IncludesCorrelationId()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            // Set correlation ID before exception handler
            app.Use(async (context, next) =>
            {
                context.Items["CorrelationId"] = "test-correlation-123";
                await next();
            });
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new ValidationException("Validation failed"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("correlationId", out var correlationIdElement).Should().BeTrue();
        correlationIdElement.GetString().Should().Be("test-correlation-123");
    }

    [Fact]
    public async Task Response_IncludesTimestamp()
    {
        // Arrange
        var fakeTime = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider(fakeTime);

        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new ValidationException("Validation failed"));
            });
        }, timeProvider: fakeTimeProvider);

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("timestamp", out var timestampElement).Should().BeTrue();
        timestampElement.GetString().Should().Contain("2025-06-15");
    }

    [Fact]
    public async Task ProductionMode_HidesDetailFor5xx()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new InvalidOperationException("Secret internal error message"));
            });
        }, environmentName: Environments.Production);

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("detail", out var detailElement).Should().BeTrue();
        detailElement.GetString().Should().NotContain("Secret internal error message");
        detailElement.GetString().Should().Contain("contact support");
    }

    [Fact]
    public async Task DevelopmentMode_IncludesDetailFor5xx()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new InvalidOperationException("Detailed error message for debugging"));
            });
        }, environmentName: Environments.Development);

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("detail", out var detailElement).Should().BeTrue();
        detailElement.GetString().Should().Be("Detailed error message for debugging");
    }

    [Fact]
    public async Task ClientError4xx_AlwaysIncludesDetail()
    {
        // Arrange - Production mode
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new ValidationException("Field email is invalid"));
            });
        }, environmentName: Environments.Production);

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert - Even in production, 4xx errors include the message
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("detail", out var detailElement).Should().BeTrue();
        detailElement.GetString().Should().Be("Field email is invalid");
    }

    [Fact]
    public async Task ValidationException_IncludesErrorsDictionary()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            ["email"] = ["Invalid format"],
            ["password"] = ["Too short"]
        };

        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new ValidationException("Validation failed", errors));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("errors", out var errorsElement).Should().BeTrue();
        errorsElement.TryGetProperty("email", out var emailErrors).Should().BeTrue();
        emailErrors[0].GetString().Should().Be("Invalid format");
    }

    [Fact]
    public async Task Response_HasCorrectContentType()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new ValidationException("Validation failed"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Response_IncludesCorrectErrorType()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                    throw new ConflictException("Already exists"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("type", out var typeElement).Should().BeTrue();
        typeElement.GetString().Should().Be("https://meridian.console/errors/conflict");
    }

    [Fact]
    public async Task Response_IncludesInstance()
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/api/users/error", context =>
                    throw new ValidationException("Validation failed"));
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/api/users/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("instance", out var instanceElement).Should().BeTrue();
        instanceElement.GetString().Should().Be("/api/users/error");
    }

    [Theory]
    [InlineData(typeof(ArgumentNullException), 400)]
    [InlineData(typeof(InvalidOperationException), 500)] // Generic programming error = 500
    [InlineData(typeof(UnauthorizedAccessException), 401)]
    [InlineData(typeof(NotImplementedException), 501)]
    [InlineData(typeof(TimeoutException), 504)]
    public async Task StandardExceptions_MapToCorrectStatusCodes(Type exceptionType, int expectedStatusCode)
    {
        // Arrange
        using var host = await CreateTestHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/error", context =>
                {
                    var ex = (Exception)Activator.CreateInstance(exceptionType, "Test error")!;
                    throw ex;
                });
            });
        });

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        ((int)response.StatusCode).Should().Be(expectedStatusCode);
    }

    [Fact]
    public async Task TraceId_UseActivityTraceIdWhenPresent()
    {
        // Arrange
        string? capturedTraceId = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Test.GlobalExceptionHandler",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var testActivitySource = new ActivitySource("Test.GlobalExceptionHandler");

        // For this test, we need to put the activity BEFORE the exception handler,
        // mimicking real OTEL instrumentation. Use custom host setup.
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .UseEnvironment("Testing")
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDhadgarErrorHandling();
                    })
                    .Configure(app =>
                    {
                        // Activity middleware MUST be BEFORE error handling
                        // This mimics how OTEL AspNetCore instrumentation works
                        app.Use(async (context, next) =>
                        {
                            var activity = testActivitySource.StartActivity("TestRequest");
                            capturedTraceId = activity?.TraceId.ToString();
                            try
                            {
                                await next();
                            }
                            finally
                            {
                                activity?.Dispose();
                            }
                        });

                        // Now error handling
                        app.UseDhadgarErrorHandling();

                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/error", context =>
                                throw new ValidationException("Test"));
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/error");

        // Assert
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("traceId", out var traceIdElement).Should().BeTrue();
        traceIdElement.GetString().Should().Be(capturedTraceId);
    }

    private static async Task<IHost> CreateTestHost(
        Action<IApplicationBuilder> configureApp,
        string environmentName = "Testing",
        TimeProvider? timeProvider = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .UseEnvironment(environmentName)
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        if (timeProvider != null)
                        {
                            services.AddSingleton(timeProvider);
                        }
                        services.AddDhadgarErrorHandling();
                    })
                    .Configure(app =>
                    {
                        app.UseDhadgarErrorHandling();
                        configureApp(app);
                    });
            })
            .StartAsync();
    }
}
