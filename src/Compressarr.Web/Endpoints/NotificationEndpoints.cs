using Microsoft.AspNetCore.Http;
using Compressarr.Core.Config;
using Compressarr.Core.Notifications;
using Compressarr.Web.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Compressarr.Web.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notifications/settings", (IConfigStore configStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            return Results.Json(new NotificationSettingsDto(config.Notifications.ToastEnabled));
        });

        app.MapPut("/api/notifications/settings", (NotificationSettingsDto dto, IConfigStore configStore) =>
        {
            configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                config.Notifications.ToastEnabled = dto.ToastEnabled;
                return true;
            });
            return Results.Ok();
        });

        app.MapGet("/api/notifications/types", (IEnumerable<INotifier> notifiers) =>
        {
            var types = notifiers.Select(n => new NotifierTypeDto(
                n.Type,
                n.DisplayName,
                n.Fields.Select(f => new NotifierFieldDto(f.Key, f.Label, f.InputType, f.Required, f.Secret, f.Options)).ToList()))
                .ToList();
            return Results.Json(types);
        });

        app.MapGet("/api/notifications/channels", (IConfigStore configStore) =>
        {
            var config = configStore.Load(AppPaths.GetConfigFilePath());
            return Results.Json(config.Notifications.Channels.Select(ConfigMapping.ToChannelDto).ToList());
        });

        app.MapPost("/api/notifications/channels", (CreateChannelRequest request, IConfigStore configStore, IEnumerable<INotifier> notifiers) =>
        {
            var notifier = notifiers.FirstOrDefault(n => string.Equals(n.Type, request.Type, StringComparison.OrdinalIgnoreCase));
            if (notifier is null) return Results.BadRequest(new { message = $"Unknown notification type '{request.Type}'." });

            var dto = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                var channel = new NotificationChannel
                {
                    Type = notifier.Type,
                    DisplayName = notifier.DisplayName
                };
                config.Notifications.Channels.Add(channel);
                return ConfigMapping.ToChannelDto(channel);
            });

            return Results.Json(dto);
        });

        app.MapPut("/api/notifications/channels/{id}", (string id, NotificationChannelDto dto, IConfigStore configStore) =>
        {
            var result = configStore.Update(AppPaths.GetConfigFilePath(), config =>
            {
                var channel = config.Notifications.Channels.FirstOrDefault(c => c.Id == id);
                if (channel is null) return null;

                ConfigMapping.ApplyChannelDto(channel, dto);
                return ConfigMapping.ToChannelDto(channel);
            });

            return result is null ? Results.NotFound() : Results.Json(result);
        });

        app.MapDelete("/api/notifications/channels/{id}", (string id, IConfigStore configStore) =>
        {
            var removed = configStore.Update(AppPaths.GetConfigFilePath(), config => config.Notifications.Channels.RemoveAll(c => c.Id == id));
            return removed == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Tests whatever's currently typed into a channel's fields, saved or not - same "test the
        // form, not the file" behavior /api/arr/test already has for Sonarr/Radarr.
        app.MapPost("/api/notifications/test", async (TestNotifierRequest request, IEnumerable<INotifier> notifiers) =>
        {
            var notifier = notifiers.FirstOrDefault(n => string.Equals(n.Type, request.Type, StringComparison.OrdinalIgnoreCase));
            if (notifier is null) return Results.Json(new { success = false, message = $"Unknown notification type '{request.Type}'." });

            var result = await notifier.TestAsync(request.Settings, CancellationToken.None);
            return Results.Json(new { success = result.Success, message = result.Message });
        });
    }
}
