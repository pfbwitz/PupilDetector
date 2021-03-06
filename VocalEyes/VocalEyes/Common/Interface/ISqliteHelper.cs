﻿using SQLite.Net;

namespace  VocalEyes.Common.Interface
{
    public interface ISqliteHelper
    {
        bool DatabaseExists(string databasename);

        void MakeDatase(string databasename);

	    void UpdateTables(string databasename);

        SQLiteConnection GetConnection(string databasename);

        string GetDatabasePath(string databasename);
    }
}
