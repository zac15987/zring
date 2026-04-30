
namespace Zring.Dto
{
    /// <summary>
    /// Container for data retrieved at background process
    /// </summary>
    internal class BackgroundData
    {
        /// <summary>
        /// Array of installed applications
        /// </summary>
        public InstalledApplication[] InstalledApplications { get; }

        /// <summary>
        /// CTOR
        /// </summary>
        /// <param name="installedApplications">Array of installed applications</param>
        public BackgroundData(InstalledApplication[] installedApplications)
        {
            InstalledApplications = installedApplications;
        }

        /// <summary>
        /// Returns string representation of the object
        /// </summary>
        /// <returns>String representation of the object</returns>
        public override string ToString()
        {
            return $"{InstalledApplications?.Length ?? 0} installed applications";
        }
    }
}
