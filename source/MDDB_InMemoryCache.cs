using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using BattleTech.Data;
using HBS.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection.Emit;
using BattleTech.Rendering;
using UnityEngine;
using System.Data;
using System.Data.SQLite;
using Dapper;

namespace BattletechPerformanceFix
{
    // source = sqlite3.connect('existing_db.db')
    // dest = sqlite3.connect(':memory:')
    // source.backup(dest)
    public class MDDB_InMemoryCache : Feature
    {
        public void Activate()
        {
            var precull = Control.CheckPatch( AccessTools.Method(typeof(BTCustomRenderer), "OnPreCull")
                                            , "2d4664901a7bde11ee58911347847642c51dd41958b7b57bf08caa9a821f017f");

            Control.Trap(() =>
            Control.harmony.Patch(AccessTools.Method(typeof(FileBackedSQLiteDB), "Open")
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), "Open")));
            Control.Trap(() =>
            Control.harmony.Patch(AccessTools.Method(typeof(FileBackedSQLiteDB), "Close")
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), "Close")));

            Control.Trap(() =>
            Control.harmony.Patch(AccessTools.Method(typeof(FileBackedSQLiteDB), "Close")
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), "Close")));

            Control.harmony.Patch(AccessTools.Method(typeof(MapsAndEncounters_MDDExtensions), "GetMapByPath")
                                 , new HarmonyMethod(typeof(MDDB_InMemoryCache), "GetMapByPath"));

        }

        public static void GetMapByPath(string path)
        {
            Control.Log("GetMapByPath: {0}", path);
        }

        public static bool Open(FileBackedSQLiteDB __instance, ref IDbConnection ___connection)
        {
            var stack = new StackTrace().ToString();
            Control.Trap(() =>
            {
               
                Control.Log("Open {0} (:type-avail {1} :stack {2}", __instance.ConnectionURI, typeof(SQLiteConnection), stack);
                if (memoryStore == null)
                {
                    Control.Log("Init in-memory store");
                    var mstore = new SQLiteConnection("Data Source=:memory:");
                    Control.Log("Open mstore");
                    mstore.Open();
                    Control.Log("sqlconn disk");
                    var disk = new SQLiteConnection(__instance.ConnectionURI);
                    Control.Log("open disk");
                    disk.Open();
                    Control.Log("backup db");
                    disk.BackupDatabase(mstore, mstore.Database, disk.Database, -1, null, -1);

                    Control.Log("make proxy");
                    memoryStore = new SQLProxy(mstore);

                    Control.Log("Try fetch data");
                    var res = memoryStore.ExecuteScalar("SELECT * FROM WeaponDef");
                    Control.Log("dbfresres {0}", res);
                    
                    Control.Log("Done");
                }
            });

            ___connection = memoryStore;

            return false;
        }

        public static bool Close()
        {
            var stack = new StackTrace().ToString();
            Control.Log("Close intercepted {0}", stack);
            return false;
        }

        static IDbConnection memoryStore = null;
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
        public void Close() { Control.Log("EFXR CLOSE"); conn.Close(); }
        public IDbCommand CreateCommand() => conn.CreateCommand();
        public void Open() { Control.Log("EFXR OPEN"); conn.Open(); }
        public string ConnectionString { get => conn.ConnectionString; set => conn.ConnectionString = value; }
        public int ConnectionTimeout { get => conn.ConnectionTimeout; }
        public string Database { get => conn.Database; }
        public ConnectionState State { get => conn.State; }
        public void Dispose() { Control.Log("EFXR DISPOSE"); conn.Dispose(); }
}
}

