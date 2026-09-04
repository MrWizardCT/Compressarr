namespace Compressarr.Web.Dtos;

public sealed record NotificationSettingsDto(bool ToastEnabled);

public sealed record NotifierFieldDto(string Key, string Label, string InputType, bool Required, bool Secret, IReadOnlyList<string>? Options);

public sealed record NotifierTypeDto(string Type, string DisplayName, IReadOnlyList<NotifierFieldDto> Fields);

public sealed record NotificationChannelDto(string Id, string Type, string DisplayName, string Trigger, Dictionary<string, string> Settings);

public sealed record CreateChannelRequest(string Type);

public sealed record TestNotifierRequest(string Type, Dictionary<string, string> Settings);
