using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Connector.Api.Endpoints;

static class HealthEndpoints
{
    internal static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/health",
            async (ExportLogDbContext logDb, IOptions<ExportSinkOptions> sinkOpts) =>
            {
                var staging = sinkOpts.Value.StagingPath;
                var stagingOk = Directory.Exists(staging) && IsStagingWritable(staging);
                var logOk = false;

                try
                {
                    logOk = await logDb.Database.CanConnectAsync();
                }
                catch
                {
                    // Intentionally swallowed — degraded health is reported in the JSON response, not thrown.
                }

                var checks = new { log_db = logOk, staging = stagingOk };
                var healthy = logOk && stagingOk;
                var result = new { status = healthy ? "healthy" : "degraded", checks };
                return healthy ? Results.Ok(result) : Results.Json(result, statusCode: 503);
            }
        );
    }

    private static bool IsStagingWritable(string path)
    {
        try
        {
            var probe = Path.Combine(path, ".health_probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
