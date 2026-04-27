using System;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo

namespace Zring.ViewModel
{
    /// <summary>
    /// Logging partial for <see cref="AppFilterViewModel"/>.
    /// </summary>
    public partial class AppFilterViewModel
    {
        #region Logging
        /// <summary>
        /// Logger used
        /// </summary>
        private readonly ILogger logger;

        //EventIds:
        // 7xxx - App filter (79xx reserved for Errors/Exceptions)

        /// <summary>
        /// Log definition options
        /// </summary>
        private static readonly LogDefineOptions LogOptions = new() { SkipEnabledCheck = true };

        //----------------------------------------------
        // 7001 LogAppFilterChanged
        //----------------------------------------------

        /// <summary>
        /// Logger message definition for LogAppFilterChanged
        /// </summary>
        private static readonly Action<ILogger, string, bool, int, Exception?> __LogAppFilterChangedDefinition =
            LoggerMessage.Define<string, bool, int>(
                LogLevel.Information,
                new EventId(7001, nameof(LogAppFilterChanged)),
                "App filter changed: key={groupKey} checked={isChecked} total_selected={totalSelected}",
                LogOptions);

        /// <summary>
        /// Logs record (Information) when the user toggles an app in the filter popup
        /// </summary>
        /// <param name="groupKey">Group key of the toggled app</param>
        /// <param name="isChecked">New checked state</param>
        /// <param name="totalSelected">Total number of selected apps after the change</param>
        private void LogAppFilterChanged(string groupKey, bool isChecked, int totalSelected)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                __LogAppFilterChangedDefinition(logger, groupKey, isChecked, totalSelected, null);
            }
        }

        //----------------------------------------------
        // 7002 LogAppFilterCleared
        //----------------------------------------------

        /// <summary>
        /// Logger message definition for LogAppFilterCleared
        /// </summary>
        private static readonly Action<ILogger, int, Exception?> __LogAppFilterClearedDefinition =
            LoggerMessage.Define<int>(
                LogLevel.Information,
                new EventId(7002, nameof(LogAppFilterCleared)),
                "App filter cleared (had {previousCount} selected)",
                LogOptions);

        /// <summary>
        /// Logs record (Information) when the user clears all filter selections
        /// </summary>
        /// <param name="previousCount">Number of apps selected before clearing</param>
        private void LogAppFilterCleared(int previousCount)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                __LogAppFilterClearedDefinition(logger, previousCount, null);
            }
        }
        #endregion
    }
}
