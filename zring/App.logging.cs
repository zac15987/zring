using System;
using Microsoft.Extensions.Logging;
// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo

namespace Zring
{
    /// <summary>
    /// <see cref="LoggerMessage.Define{T}"/>-based log declarations for <see cref="App"/>.
    /// </summary>
    public partial class App
    {
        #region Logging
        // 8xxx - App host (89xx Errors/Exceptions)

        /// <summary>
        /// Log definition options
        /// </summary>
        private static readonly LogDefineOptions LogOptions = new() { SkipEnabledCheck = true };

        //----------------------------------------------
        // 8001 App started
        //----------------------------------------------

        /// <summary>
        /// Logger message definition for LogAppStarted
        /// </summary>
        private static readonly Action<ILogger, string, string, string, string, Exception?> __LogAppStartedDefinition =
            LoggerMessage.Define<string, string, string, string>(
                LogLevel.Information,
                new EventId(8001, nameof(LogAppStarted)),
                "App started (language={language}, jumpList={jumpListVariant}, audio={audioVariant}, startup={startupVariant})",
                LogOptions);

        /// <summary>
        /// Logs record (Information) after the host is built, DI is wired and the main window is shown.
        /// Captures the resolved language and the concrete service variants picked by feature flags.
        /// </summary>
        /// <param name="language">Resolved UI language (e.g. "en", "cs")</param>
        /// <param name="jumpListVariant">Concrete <see cref="Win32.Services.JumpLists.IJumpListService"/> implementation name</param>
        /// <param name="audioVariant">Concrete <see cref="Win32.Services.Audio.IAudioService"/> implementation name</param>
        /// <param name="startupVariant">Concrete <see cref="Win32.Services.Startup.IStartupService"/> implementation name</param>
        private void LogAppStarted(string language, string jumpListVariant, string audioVariant, string startupVariant)
        {
            if (logger != null && logger.IsEnabled(LogLevel.Information))
            {
                __LogAppStartedDefinition(logger, language, jumpListVariant, audioVariant, startupVariant, null);
            }
        }

        //----------------------------------------------
        // 8002 App host stopping
        //----------------------------------------------

        /// <summary>
        /// Logger message definition for LogAppHostStopping
        /// </summary>
        private static readonly Action<ILogger, Exception?> __LogAppHostStoppingDefinition =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(8002, nameof(LogAppHostStopping)),
                "App host stopping",
                LogOptions);

        /// <summary>
        /// Logs record (Information) when the app is exiting and the host is about to be stopped.
        /// </summary>
        private void LogAppHostStopping()
        {
            if (logger != null && logger.IsEnabled(LogLevel.Information))
            {
                __LogAppHostStoppingDefinition(logger, null);
            }
        }

        #endregion
    }
}
