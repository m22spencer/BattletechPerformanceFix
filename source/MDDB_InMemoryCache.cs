using System;
using Harmony;
using BattleTech.Data;
using HBS.Data;
using System.IO;
using System.Data;
using System.Data.SQLite;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    // source = sqlite3.connect('existing_db.db')
    // dest = sqlite3.connect(':memory:')
    // source.backup(dest)
    public class MDDB_InMemoryCache : Feature
    {
        public void Activate()
        {
            var modtek = Main.ModTekType;
            LogInfo($"Found modtek? {modtek?.FullName}");

            if (modtek == null)
                throw new Exception("Aborting MDDB_InMEmoryCache patch: Could not find ModTek");

            var path = System.Environment.GetEnvironmentVariable("PATH");
            var editor_folder = Path.GetFullPath(Main.ModDir); // "./BattleTech_Data/StreamingAssets/editor");
            System.Environment.SetEnvironmentVariable("PATH", path + $";{editor_folder}");

            try { new SQLiteConnection("Data Source=:memory:"); }
            catch(Exception e) { LogWarning("SQlite dependencies not found. Aborting MDDB patch.");
                                 LogException(e);
                                 throw new Exception("MDDB Patch aborted (This is okay, you just won't get the performance improvement)"); }
            var harmony = Main.harmony;

            Trap(() =>
            harmony.Patch(AccessTools.Method(typeof(FileBackedSQLiteDB), "Open")
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), "Open")));
            Trap(() =>
            harmony.Patch(AccessTools.Method(typeof(FileBackedSQLiteDB), "Close")
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), "Close")));

            Trap(() =>
            harmony.Patch(AccessTools.Method(typeof(FileBackedSQLiteDB), "Close")
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), "Close")));

            harmony.Patch(AccessTools.Method(typeof(MapsAndEncounters_MDDExtensions), "GetMapByPath")
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), "GetMapByPath"));

            

            harmony.Patch(AccessTools.Method(modtek, "WriteJsonFile")
                                 , null
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), nameof(MDDB_InMemoryCache.SaveToDisk)));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.OnGameShutdown), "ShutdownFileIO")
                                 , null
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), nameof(MDDB_InMemoryCache.SaveToDisk)));
        }

        public static bool Open(FileBackedSQLiteDB __instance, ref IDbConnection ___connection)
        {
            Trap(() =>
            {
                if (ConnectionURI != null && ConnectionURI != __instance.ConnectionURI)
                {
                    LogError($"MDDB_InMemoryCache: Expected {ConnectionURI} but got {__instance.ConnectionURI}");
                }
                if (memoryStore == null)
                {

                    ConnectionURI = __instance.ConnectionURI;
                    LogInfo($"MDDB_InMemoryCache Open {ConnectionURI} -> :memory:");
                    mstore = new SQLiteConnection("Data Source=:memory:");
                    mstore.Open();
                    var disk = new SQLiteConnection(__instance.ConnectionURI);
                    disk.Open();
                    disk.BackupDatabase(mstore, mstore.Database, disk.Database, -1, null, -1);
                    disk.Close();
                    
                    memoryStore = new SQLProxy(mstore);
                }
            });

            ___connection = memoryStore;

            return false;
        }

        public static void SaveToDisk()
        {
            Trap(() =>
            {
                if (ConnectionURI == null)
                {
                    LogWarning("Tried to save MDDB but no connection info");
                    return;
                }
                
                LogInfo($"MDDB_InMemoryCache Write :memory: {ConnectionURI}");
                var disk = new SQLiteConnection(ConnectionURI);
                disk.Open();
                
                mstore.BackupDatabase(disk, disk.Database, mstore.Database, -1, null, -1);
                disk.Close();
            });
        }

        public static bool Close()
        {
            return false;
        }

        static SQLiteConnection mstore = null;
        static IDbConnection memoryStore = null;
        static string ConnectionURI = null;
    }

    class SQLProxy : IDbConnection
    {
        SQLiteConnection conn;
        public SQLProxy(SQLiteConnection conn)
        {
            this.conn = conn;
        }
        public IDbTransaction BeginTransaction() => conn.BeginTransaction();
        public IDbTransaction BeginTransaction(IsolationLevel il) => conn.BeginTransaction(il);
        public void ChangeDatabase(string databaseName) => conn.ChangeDatabase(databaseName);
        public void Close() { LogSpam("EFXR CLOSE"); conn.Close(); }
        public IDbCommand CreateCommand() => conn.CreateCommand();
        public void Open() { LogSpam("EFXR OPEN"); conn.Open(); }
        public string ConnectionString { get => conn.ConnectionString; set => conn.ConnectionString = value; }
        public int ConnectionTimeout { get => conn.ConnectionTimeout; }
        public string Database { get => conn.Database; }
        public ConnectionState State { get => conn.State; }
        public void Dispose() { LogSpam("EFXR DISPOSE"); conn.Dispose(); }
}
}

