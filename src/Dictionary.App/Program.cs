using PortableDictionary.Core;
using PortableDictionary.Forms;
using PortableDictionary.Services;

namespace PortableDictionary;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        MainForm? mainForm = null;
        using var singleInstance = SingleInstance.Start(() => mainForm?.RequestRestore());
        if (!singleInstance.IsFirst)
        {
            return 0;
        }

        try
        {
            var baseDirectory = AppContext.BaseDirectory;
            var databasePath = Path.Combine(baseDirectory, "data", "dictionary.db");
            using var repository = new DictionaryRepository(databasePath);
            var settingsStore = new PortableSettingsStore(Path.Combine(baseDirectory, "settings.json"));
            var settingsLoad = settingsStore.Load();
            var lookup = new LookupService(repository);
            mainForm = new MainForm(lookup, repository.DatasetInfo, settingsStore, settingsLoad);
            Application.Run(mainForm);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException or UnauthorizedAccessException
            or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(
                "Dictionary could not open or validate its prepared offline data.\r\n\r\n" + ex.Message +
                "\r\n\r\nRestore data/dictionary.db from the portable release, or rebuild it with the supplied scripts.",
                "Dictionary data error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }
        finally
        {
            mainForm?.Dispose();
        }
    }
}
