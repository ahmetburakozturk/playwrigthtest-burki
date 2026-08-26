using Microsoft.Extensions.Logging;
using PlaywrightSmartRecorder.Core;
using PlaywrightSmartRecorder.Parser;
using PlaywrightSmartRecorder.Core.Services;

namespace PlaywrightSmartRecorder;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<PlaywrightRecorderService>();
		builder.Services.AddSingleton<PlaywrightSmartRecorder.Parser.TypeScriptGenerator>();
		builder.Services.AddSingleton<PlaywrightSmartRecorder.Core.Services.LogService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
