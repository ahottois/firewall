using System.Collections.Concurrent;
using System.Text.Json;
using NetworkFirewall.Models;
using NetworkFirewall.Services;

namespace NetworkFirewall.Controllers;

/// <summary>
/// Endpoint pour les notifications en temps réel via Server-Sent Events
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/notifications/stream", async (
            HttpContext context,
            INotificationService notificationService,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("Connection", "keep-alive");

            var alertQueue = new ConcurrentQueue<NetworkAlert>();

            void alertHandler(object? sender, NetworkAlert alert)
            {
                alertQueue.Enqueue(alert);
            }

            notificationService.AlertReceived += alertHandler;

            try
            {
                // Envoyer un ping initial
                await context.Response.WriteAsync($"event: connected\n");
                await context.Response.WriteAsync($"data: {{\"status\": \"connected\"}}\n\n");
                await context.Response.Body.FlushAsync();

                // Garder la connexion ouverte
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Traiter les alertes en file d'attente
                    while (alertQueue.TryDequeue(out var alert))
                    {
                        try
                        {
                            var json = JsonSerializer.Serialize(alert, new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                            });
                            
                            await context.Response.WriteAsync($"event: alert\n");
                            await context.Response.WriteAsync($"data: {json}\n\n");
                            await context.Response.Body.FlushAsync();
                        }
                        catch
                        {
                            // Client disconnected
                            return;
                        }
                    }

                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal disconnection
            }
            finally
            {
                notificationService.AlertReceived -= alertHandler;
            }
        });
    }
}
