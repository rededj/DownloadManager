namespace DownloadManager
{
    public class AppSettings
    {
    public string DownloadDirectory { get; set; }
    
        public AppSettings() {
        DownloadDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "DownloadManager" );
        }
    }
}