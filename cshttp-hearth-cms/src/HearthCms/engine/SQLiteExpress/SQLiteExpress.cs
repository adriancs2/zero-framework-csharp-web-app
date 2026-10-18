using System;
using System.Collections.Generic;
using System.Data;

using System.Text;
using System.Globalization;
using System.Reflection;

namespace System.Data.SQLite
{
    public class SQLiteExpress
    {
        public const string Version = "1.0.0";


        public SQLiteCommand cmd;

        public SQLiteExpress()
        {

        }

        public SQLiteExpress(SQLiteCommand cmd)
        {
            this.cmd = cmd;
        }

        #region Transaction

        public void BeginTransaction()
        {
            cmd.CommandText = "begin transaction;";
            cmd.ExecuteNonQuery();
        }

        public void Commit()
        {
            cmd.CommandText = "commit;";
            cmd.ExecuteNonQuery();
        }

        public void Rollback()
        {
            cmd.CommandText = "rollback;";
            cmd.ExecuteNonQuery();
        }

        #endregion

        #region Select

        public DataTable Select(string sql)
        {
            return SelectParam(sql, null);
        }

        public DataTable Select(string sql, IDictionary<string, object> dicParameters)
        {
            if (dicParameters == null)
            {
                return SelectParam(sql, null);
            }

            List<SQLiteParameter> lst = GetParametersList(dicParameters);
            return SelectParam(sql, lst);
        }

        public DataTable Select(string sql, IEnumerable<SQLiteParameter> parameters)
        {
            return SelectParam(sql, parameters);
        }

        DataTable SelectParam(string sql, IEnumerable<SQLiteParameter> parameters)
        {
            cmd.CommandText = sql;
            cmd.Parameters.Clear();

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.Add(param);
                }
            }

            SQLiteDataAdapter da = new SQLiteDataAdapter(cmd);
            DataTable dt = new DataTable();

            da.Fill(dt);
            return dt;
        }

        #endregion

        #region Execute

        public void Execute(string sql)
        {
            ExecuteParam(sql, null);
        }

        public void Execute(string sql, IDictionary<string, object> dicParameters)
        {
            if (dicParameters == null)
            {
                ExecuteParam(sql, null);
            }
            else
            {
                List<SQLiteParameter> lst = GetParametersList(dicParameters);
                ExecuteParam(sql, lst);
            }
        }

        public void Execute(string sql, IEnumerable<SQLiteParameter> parameters)
        {
            ExecuteParam(sql, parameters);
        }

        void ExecuteParam(string sql, IEnumerable<SQLiteParameter> parameters)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = sql;

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.Add(param);
                }
            }

            cmd.ExecuteNonQuery();
        }

        #endregion

        #region ExecuteScalar

        public object ExecuteScalar(string sql)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }

        public object ExecuteScalar(string sql, IDictionary<string, object> dicParameters)
        {
            List<SQLiteParameter> parameters = GetParametersList(dicParameters);
            return ExecuteScalar(sql, parameters);
        }

        public object ExecuteScalar(string sql, IEnumerable<SQLiteParameter> parameters)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = sql;

            if (parameters != null)
            {
                foreach (var parameter in parameters)
                {
                    cmd.Parameters.Add(parameter);
                }
            }

            return cmd.ExecuteScalar();
        }

        public T ExecuteScalar<T>(string sql)
        {
            return ExecuteScalarParam<T>(sql, null);
        }

        public T ExecuteScalar<T>(string sql, IDictionary<string, object> dicParameters)
        {
            if (dicParameters == null)
            {
                return ExecuteScalarParam<T>(sql, null);
            }

            List<SQLiteParameter> parameters = GetParametersList(dicParameters);

            return ExecuteScalarParam<T>(sql, parameters);
        }

        public T ExecuteScalar<T>(string sql, IEnumerable<SQLiteParameter> parameters)
        {
            return ExecuteScalarParam<T>(sql, parameters);
        }

        T ExecuteScalarParam<T>(string sql, IEnumerable<SQLiteParameter> parameters)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = sql;

            if (parameters != null)
            {
                foreach (var parameter in parameters)
                {
                    cmd.Parameters.Add(parameter);
                }
            }

            DataTable dt = SelectParam(sql, parameters);

            if (dt.Rows.Count == 0)
            {
                return (T)GetValue(null, typeof(T));
            }

            object ob = dt.Rows[0][0];

            try
            {
                return (T)GetValue(ob, typeof(T));
            }
            catch { }

            return (T)Convert.ChangeType(ob, typeof(T));
        }

        #endregion

        #region Parameters Helper

        private List<SQLiteParameter> GetParametersList(IDictionary<string, object> dicParameters)
        {
            List<SQLiteParameter> lst = new List<SQLiteParameter>();
            if (dicParameters != null)
            {
                foreach (KeyValuePair<string, object> kv in dicParameters)
                {
                    lst.Add(new SQLiteParameter(kv.Key, kv.Value));
                }
            }
            return lst;
        }

        #endregion

        #region Insert

        public void Insert(string tableName, Dictionary<string, object> dic)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("insert into `");
            sb.Append(tableName);
            sb.Append("` (");

            bool isFirst = true;

            foreach (var kv in dic)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    sb.Append(",");
                }

                sb.Append("`");
                sb.Append(kv.Key);
                sb.Append("`");
            }

            sb.Append(") values(");

            isFirst = true;

            foreach (var kv in dic)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    sb.Append(",");
                }

                sb.Append("@");
                sb.Append(kv.Key);
            }

            sb.Append(");");

            cmd.CommandText = sb.ToString();

            cmd.Parameters.Clear();

            foreach (var kv in dic)
            {
                cmd.Parameters.AddWithValue($"@{kv.Key}", kv.Value);
            }

            cmd.ExecuteNonQuery();
        }

        public long LastInsertId
        {
            get
            {
                return ExecuteScalar<long>("select last_insert_rowid();");
            }
        }

        #endregion

        #region Save / SaveList

        /// <summary>
        /// Performs Insert or Replace of single Class Object
        /// </summary>
        /// <param name="table">Table's Name</param>
        /// <param name="classObject">Any class object</param>
        public void Save(string table, object classObject)
        {
            List<object> lst = new List<object>();
            lst.Add(classObject);

            SaveList(table, lst);
        }

        /// <summary>
        /// Performs Insert or Replace of List of Class Objects
        /// </summary>
        /// <param name="table">Table name</param>
        /// <param name="lst">List of same class object</param>
        public void SaveList<T>(string table, IEnumerable<T> lst)
        {
            // Grab the first element (and bail on an empty sequence) without LINQ.
            T first = default(T);
            bool any = false;
            foreach (var item in lst) { first = item; any = true; break; }
            if (!any)
                return;

            table = Escape(table);

            DataTable dt = Select($"pragma table_info(`{table}`);");

            List<string> lstCol = new List<string>();

            foreach (DataRow dr in dt.Rows)
            {
                lstCol.Add(dr["name"] + "");
            }

            var fields = first.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var properties = first.GetType().GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            foreach (var s in lst)
            {
                Dictionary<string, object> dic = new Dictionary<string, object>();

                foreach (var col in lstCol)
                {
                    foreach (var field in fields)
                    {
                        if (col == field.Name)
                        {
                            dic[col] = field.GetValue(s);
                            break;
                        }
                    }

                    foreach (var prop in properties)
                    {
                        if (col == prop.Name)
                        {
                            dic[col] = prop.GetValue(s);
                            break;
                        }
                    }
                }

                InsertOrReplace(table, dic);
            }
        }

        #endregion

        #region InsertOrReplace / InsertUpdate (Upsert)

        /// <summary>
        /// Insert or Replace: replaces the entire row if a conflict on primary key is found.
        /// Uses SQLite's INSERT OR REPLACE syntax.
        /// </summary>
        public void InsertOrReplace(string tableName, Dictionary<string, object> dic)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("insert or replace into `");
            sb.Append(tableName);
            sb.Append("` (");

            bool isFirst = true;

            foreach (var kv in dic)
            {
                if (isFirst)
                    isFirst = false;
                else
                    sb.Append(",");

                sb.Append("`");
                sb.Append(kv.Key);
                sb.Append("`");
            }

            sb.Append(") values(");

            isFirst = true;

            foreach (var kv in dic)
            {
                if (isFirst)
                    isFirst = false;
                else
                    sb.Append(",");

                sb.Append("@v");
                sb.Append(kv.Key);
            }

            sb.Append(");");

            cmd.CommandText = sb.ToString();

            cmd.Parameters.Clear();

            foreach (var kv in dic)
            {
                cmd.Parameters.AddWithValue("@v" + kv.Key, kv.Value);
            }

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Upsert: Insert if not exists, Update specific columns if exists.
        /// Uses SQLite's ON CONFLICT DO UPDATE syntax (requires SQLite 3.24+).
        /// </summary>
        /// <param name="table">Table name</param>
        /// <param name="dic">All column data including primary key columns</param>
        /// <param name="lstUpdateCols">Columns to update on conflict</param>
        public void InsertUpdate(string table, Dictionary<string, object> dic, IEnumerable<string> lstUpdateCols)
        {
            // Get primary key columns from table info
            DataTable dtInfo = Select($"pragma table_info(`{table}`);");
            List<string> lstPk = new List<string>();
            foreach (DataRow dr in dtInfo.Rows)
            {
                if (Convert.ToInt32(dr["pk"]) > 0)
                {
                    lstPk.Add(dr["name"] + "");
                }
            }

            StringBuilder sb = new StringBuilder();

            sb.Append("insert into `");
            sb.Append(table);
            sb.Append("`(");

            bool isFirst = true;

            foreach (KeyValuePair<string, object> kv in dic)
            {
                if (isFirst)
                    isFirst = false;
                else
                    sb.Append(",");

                sb.Append("`");
                sb.Append(kv.Key);
                sb.Append("`");
            }

            sb.Append(")values(");

            isFirst = true;

            foreach (KeyValuePair<string, object> kv in dic)
            {
                if (isFirst)
                    isFirst = false;
                else
                    sb.Append(",");

                sb.Append("@v");
                sb.Append(kv.Key);
            }

            sb.Append(") on conflict(");

            isFirst = true;

            foreach (string pk in lstPk)
            {
                if (isFirst)
                    isFirst = false;
                else
                    sb.Append(",");

                sb.Append("`");
                sb.Append(pk);
                sb.Append("`");
            }

            sb.Append(") do update set ");

            isFirst = true;

            foreach (string key in lstUpdateCols)
            {
                if (isFirst)
                    isFirst = false;
                else
                    sb.Append(",");

                sb.Append("`");
                sb.Append(key);
                sb.Append("`=@v");
                sb.Append(key);
            }

            sb.Append(";");

            cmd.CommandText = sb.ToString();

            cmd.Parameters.Clear();

            foreach (KeyValuePair<string, object> kv in dic)
            {
                cmd.Parameters.AddWithValue("@v" + kv.Key, kv.Value);
            }

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Upsert with include/exclude mode for update columns.
        /// </summary>
        /// <param name="table">Table name</param>
        /// <param name="dic">All column data</param>
        /// <param name="lstCols">Column list</param>
        /// <param name="include">true = only update columns in lstCols; false = update all EXCEPT columns in lstCols</param>
        public void InsertUpdate(string table, Dictionary<string, object> dic, IEnumerable<string> lstCols, bool include)
        {
            if (include)
            {
                InsertUpdate(table, dic, lstCols);
            }
            else
            {
                List<string> lstup = new List<string>();

                foreach (var kv in dic)
                {
                    bool found = false;
                    foreach (var c in lstCols)
                    {
                        if (c == kv.Key) { found = true; break; }
                    }
                    if (!found)
                    {
                        lstup.Add(kv.Key);
                    }
                }

                InsertUpdate(table, dic, lstup);
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// Update single row
        /// </summary>
        public void Update(string tableName, Dictionary<string, object> dicData, string colCond, object varCond)
        {
            Dictionary<string, object> dic = new Dictionary<string, object>();
            dic[colCond] = varCond;
            Update(tableName, dicData, dic);
        }

        /// <summary>
        /// Update rows with single condition. updateSingleRow=true adds LIMIT 1.
        /// </summary>
        public void Update(string tableName, Dictionary<string, object> dicData, string colCond, object varCond, bool updateSingleRow)
        {
            Dictionary<string, object> dic = new Dictionary<string, object>();
            dic[colCond] = varCond;
            Update(tableName, dicData, dic, updateSingleRow);
        }

        /// <summary>
        /// Update single row with multiple conditions
        /// </summary>
        public void Update(string tableName, Dictionary<string, object> dicData, Dictionary<string, object> dicCond)
        {
            Update(tableName, dicData, dicCond, true);
        }

        /// <summary>
        /// Update rows with multiple conditions. updateSingleRow=true adds LIMIT 1.
        /// </summary>
        public void Update(string tableName, Dictionary<string, object> dicData, Dictionary<string, object> dicCond, bool updateSingleRow)
        {
            cmd.Parameters.Clear();

            if (dicData.Count == 0)
                throw new Exception("dicData is empty.");

            StringBuilder sbData = new System.Text.StringBuilder();

            sbData.Append("update `");
            sbData.Append(tableName);
            sbData.Append("` set ");

            bool firstRecord = true;

            foreach (KeyValuePair<string, object> kv in dicData)
            {
                if (firstRecord)
                    firstRecord = false;
                else
                    sbData.Append(",");

                sbData.Append("`");
                sbData.Append(kv.Key);
                sbData.Append("` = ");

                sbData.Append("@v");
                sbData.Append(kv.Key);
            }

            sbData.Append(" where ");

            firstRecord = true;

            foreach (KeyValuePair<string, object> kv in dicCond)
            {
                if (firstRecord)
                    firstRecord = false;
                else
                {
                    sbData.Append(" and ");
                }

                sbData.Append("`");
                sbData.Append(kv.Key);
                sbData.Append("` = ");

                sbData.Append("@c");
                sbData.Append(kv.Key);
            }

            if (updateSingleRow)
                sbData.Append(" limit 1;");
            else
                sbData.Append(";");

            cmd.CommandText = sbData.ToString();

            foreach (KeyValuePair<string, object> kv in dicData)
            {
                cmd.Parameters.AddWithValue("@v" + kv.Key, kv.Value);
            }

            foreach (KeyValuePair<string, object> kv in dicCond)
            {
                cmd.Parameters.AddWithValue("@c" + kv.Key, kv.Value);
            }

            cmd.ExecuteNonQuery();
        }

        #endregion

        #region GetObject / GetObjectList

        public T GetObject<T>(string sql)
        {
            DataTable dt = Select(sql);
            return Bind<T>(dt);
        }

        public T GetObject<T>(string sql, IDictionary<string, object> dicParameters)
        {
            DataTable dt = Select(sql, dicParameters);
            return Bind<T>(dt);
        }

        public T GetObject<T>(string sql, IEnumerable<SQLiteParameter> parameters)
        {
            DataTable dt = Select(sql, parameters);
            return Bind<T>(dt);
        }

        public void GetObject<T>(string sql, T ob)
        {
            DataTable dt = Select(sql);
            Bind<T>(dt, ob);
        }

        public void GetObject<T>(string sql, T ob, IDictionary<string, object> dicParameters)
        {
            DataTable dt = Select(sql, dicParameters);
            Bind<T>(dt, ob);
        }

        public void GetObject<T>(string sql, T ob, IEnumerable<SQLiteParameter> parameters)
        {
            DataTable dt = Select(sql, parameters);
            Bind<T>(dt, ob);
        }

        public List<T> GetObjectList<T>(string sql)
        {
            DataTable dt = Select(sql);
            return BindList<T>(dt);
        }

        public List<T> GetObjectList<T>(string sql, IDictionary<string, object> dicParameters)
        {
            DataTable dt = Select(sql, dicParameters);
            return BindList<T>(dt);
        }

        public List<T> GetObjectList<T>(string sql, IEnumerable<SQLiteParameter> parameters)
        {
            DataTable dt = Select(sql, parameters);
            return BindList<T>(dt);
        }

        #endregion

        #region Bind / BindList / GetValue

        static T Bind<T>(DataTable dt)
        {
            List<T> lst = BindList<T>(dt);
            if (lst.Count == 0)
            {
                return Activator.CreateInstance<T>();
            }
            return lst[0];
        }

        static void Bind<T>(DataTable dt, T ob)
        {
            if (dt.Rows.Count == 0)
                return;

            DataRow dr = dt.Rows[0];

            var fields = typeof(T).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var properties = typeof(T).GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            foreach (var fieldInfo in fields)
            {
                foreach (DataColumn dc in dt.Columns)
                {
                    if (fieldInfo.Name == dc.ColumnName)
                    {
                        fieldInfo.SetValue(ob, GetValue(dr[dc.ColumnName], fieldInfo.FieldType));
                        break;
                    }
                }
            }

            foreach (var propertyInfo in properties)
            {
                if (!propertyInfo.CanWrite)
                    continue;

                foreach (DataColumn dc in dt.Columns)
                {
                    if (propertyInfo.Name == dc.ColumnName)
                    {
                        propertyInfo.SetValue(ob, GetValue(dr[dc.ColumnName], propertyInfo.PropertyType));
                        break;
                    }
                }
            }
        }

        static List<T> BindList<T>(DataTable dt)
        {
            var fields = typeof(T).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var properties = typeof(T).GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            List<T> lst = new List<T>();

            foreach (DataRow dr in dt.Rows)
            {
                var ob = Activator.CreateInstance<T>();

                foreach (var fieldInfo in fields)
                {
                    foreach (DataColumn dc in dt.Columns)
                    {
                        if (fieldInfo.Name == dc.ColumnName)
                        {
                            fieldInfo.SetValue(ob, GetValue(dr[dc.ColumnName], fieldInfo.FieldType));
                            break;
                        }
                    }
                }

                foreach (var propertyInfo in properties)
                {
                    if (!propertyInfo.CanWrite)
                        continue;

                    foreach (DataColumn dc in dt.Columns)
                    {
                        if (propertyInfo.Name == dc.ColumnName)
                        {
                            propertyInfo.SetValue(ob, GetValue(dr[dc.ColumnName], propertyInfo.PropertyType));
                            break;
                        }
                    }
                }

                lst.Add(ob);
            }

            return lst;
        }

        static object GetValue(object ob, Type t)
        {
            if (t == typeof(string))
            {
                return ob + "";
            }
            else if (t == typeof(bool))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return false;
                return Convert.ToBoolean(ob);
            }
            else if (t == typeof(byte))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0;
                return Convert.ToByte(ob);
            }
            else if (t == typeof(sbyte))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0;
                return Convert.ToSByte(ob);
            }
            else if (t == typeof(short))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0;
                return Convert.ToInt16(ob);
            }
            else if (t == typeof(ushort))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0;
                return Convert.ToUInt16(ob);
            }
            else if (t == typeof(int))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0;
                return Convert.ToInt32(ob);
            }
            else if (t == typeof(uint))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0;
                return Convert.ToUInt32(ob);
            }
            else if (t == typeof(long))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0L;
                return Convert.ToInt64(ob);
            }
            else if (t == typeof(ulong))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0L;
                return Convert.ToUInt64(ob);
            }
            else if (t == typeof(float))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0F;
                return Convert.ToSingle(ob);
            }
            else if (t == typeof(double))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0D;
                return Convert.ToDouble(ob, CultureInfo.InvariantCulture);
            }
            else if (t == typeof(decimal))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return 0m;
                return Convert.ToDecimal(ob, CultureInfo.InvariantCulture);
            }
            else if (t == typeof(char))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return Convert.ToChar("");
                return Convert.ToChar(ob);
            }
            else if (t == typeof(DateTime))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return DateTime.MinValue;
                return Convert.ToDateTime(ob, CultureInfo.InvariantCulture);
            }
            else if (t == typeof(byte[]))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return null;

                return (byte[])ob;
            }
            else if (t == typeof(Guid))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return null;

                return (Guid)ob;
            }
            else if (t == typeof(TimeSpan))
            {
                if (ob == null || ob.GetType() == typeof(DBNull))
                    return null;

                return (TimeSpan)ob;
            }

            return Convert.ChangeType(ob, t);
        }

        #endregion

        #region String Helpers

        public string Escape(string data)
        {
            data = data.Replace("'", "''");
            return data;
        }

        public string GetLikeString(string input)
        {
            return GetLikeString(input, false);
        }

        public string GetLikeString(string input, bool escapeSqlStringSequence)
        {
            string[] sa = input.Split(' ');
            StringBuilder sb = new StringBuilder();
            foreach (string s in sa)
            {
                sb.Append("%");
                if (escapeSqlStringSequence)
                    sb.Append(Escape(s));
                else
                    sb.Append(s);
            }
            sb.Append("%");
            return sb.ToString();
        }

        public void GenerateContainsString(string columnName, string value, StringBuilder sb, Dictionary<string, object> dicParameters)
        {
            string[] sa = value.Trim().Split(' ');

            for (int i = 0; i < sa.Length; i++)
            {
                string paramName = $"@cs{columnName}{i}";

                string paramValue = sa[i].Trim();

                if (!paramValue.StartsWith("%"))
                    paramValue = "%" + paramValue;

                if (!paramValue.EndsWith("%"))
                    paramValue += "%";

                dicParameters[paramName] = paramValue;

                if (i == 0)
                    sb.Append(" and (");
                else
                    sb.Append(" and ");

                sb.Append($"`{columnName}` like {paramName}");
            }
            sb.Append(")");
        }

        #endregion

        #region DB Info

        public DataTable GetTableStatus()
        {
            return Select("select * from sqlite_master;");
        }

        public List<string> GetTableList()
        {
            DataTable dt = Select("select name from sqlite_master where type='table' and name != 'sqlite_sequence' order by name;");

            List<string> lst = new List<string>();

            foreach (DataRow dr in dt.Rows)
            {
                lst.Add(dr[0] + "");
            }

            return lst;
        }

        public DataTable GetColumnStatus(string tableName)
        {
            return Select(string.Format("pragma table_info(`{0}`);", tableName));
        }

        public DataTable ShowDatabase()
        {
            return Select("pragma database_list;");
        }

        #endregion

        #region Table Operations

        public void CreateTable(SQLiteTable table)
        {
            StringBuilder sb = new Text.StringBuilder();
            sb.Append("create table if not exists `");
            sb.Append(table.TableName);
            sb.AppendLine("`(");

            bool firstRecord = true;

            foreach (SQLiteColumn col in table.Columns)
            {
                if (col.ColumnName.Trim().Length == 0)
                {
                    throw new Exception("Column name cannot be blank.");
                }

                if (firstRecord)
                    firstRecord = false;
                else
                    sb.AppendLine(",");

                sb.Append(col.ColumnName);
                sb.Append(" ");

                if (col.AutoIncrement)
                {
                    sb.Append("integer primary key autoincrement");
                    continue;
                }

                switch (col.ColDataType)
                {
                    case ColType.Text:
                        sb.Append("text"); break;
                    case ColType.Integer:
                        sb.Append("integer"); break;
                    case ColType.Decimal:
                        sb.Append("decimal"); break;
                    case ColType.DateTime:
                        sb.Append("datetime"); break;
                    case ColType.BLOB:
                        sb.Append("blob"); break;
                }

                if (col.PrimaryKey)
                    sb.Append(" primary key");
                else if (col.NotNull)
                    sb.Append(" not null");
                else if (col.DefaultValue.Length > 0)
                {
                    sb.Append(" default ");

                    if (col.DefaultValue.Contains(" ") || col.ColDataType == ColType.Text || col.ColDataType == ColType.DateTime)
                    {
                        sb.Append("'");
                        sb.Append(col.DefaultValue);
                        sb.Append("'");
                    }
                    else
                    {
                        sb.Append(col.DefaultValue);
                    }
                }
            }

            sb.AppendLine(");");

            cmd.CommandText = sb.ToString();
            cmd.ExecuteNonQuery();
        }

        public void RenameTable(string tableFrom, string tableTo)
        {
            cmd.CommandText = string.Format("alter table `{0}` rename to `{1}`;", tableFrom, tableTo);
            cmd.ExecuteNonQuery();
        }

        public void CopyAllData(string tableFrom, string tableTo)
        {
            DataTable dt1 = Select(string.Format("select * from `{0}` where 1 = 2;", tableFrom));
            DataTable dt2 = Select(string.Format("select * from `{0}` where 1 = 2;", tableTo));

            Dictionary<string, bool> dic = new Dictionary<string, bool>();

            foreach (DataColumn dc in dt1.Columns)
            {
                if (dt2.Columns.Contains(dc.ColumnName))
                {
                    if (!dic.ContainsKey(dc.ColumnName))
                    {
                        dic[dc.ColumnName] = true;
                    }
                }
            }

            foreach (DataColumn dc in dt2.Columns)
            {
                if (dt1.Columns.Contains(dc.ColumnName))
                {
                    if (!dic.ContainsKey(dc.ColumnName))
                    {
                        dic[dc.ColumnName] = true;
                    }
                }
            }

            StringBuilder sb = new Text.StringBuilder();

            foreach (KeyValuePair<string, bool> kv in dic)
            {
                if (sb.Length > 0)
                    sb.Append(",");

                sb.Append("`");
                sb.Append(kv.Key);
                sb.Append("`");
            }

            StringBuilder sb2 = new Text.StringBuilder();
            sb2.Append("insert into `");
            sb2.Append(tableTo);
            sb2.Append("`(");
            sb2.Append(sb.ToString());
            sb2.Append(") select ");
            sb2.Append(sb.ToString());
            sb2.Append(" from `");
            sb2.Append(tableFrom);
            sb2.Append("`;");

            cmd.CommandText = sb2.ToString();
            cmd.ExecuteNonQuery();
        }

        public void DropTable(string table)
        {
            cmd.CommandText = string.Format("drop table if exists `{0}`", table);
            cmd.ExecuteNonQuery();
        }

        public void UpdateTableStructure(string targetTable, SQLiteTable newStructure)
        {
            newStructure.TableName = targetTable + "_temp";

            CreateTable(newStructure);

            CopyAllData(targetTable, newStructure.TableName);

            DropTable(targetTable);

            RenameTable(newStructure.TableName, targetTable);
        }

        public void AttachDatabase(string database, string alias)
        {
            Execute(string.Format("attach '{0}' as {1};", database, alias));
        }

        public void DetachDatabase(string alias)
        {
            Execute(string.Format("detach {0};", alias));
        }

        #endregion

        #region Code Generation

        public string GenerateCustomClassField(string sql, FieldsOutputType _fieldOutputType)
        {
            return GenerateClassField(sql, _fieldOutputType);
        }

        public string GenerateTableClassFields(string tablename, FieldsOutputType _fieldOutputType)
        {
            return GenerateClassField($"select * from `{tablename}` where 1=0;", _fieldOutputType);
        }

        public string GenerateTableDictionaryEntries(string tablename)
        {
            DataTable dt = Select($"pragma table_info(`{tablename}`);");

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Dictionary<string, object> dic = new Dictionary<string, object>();");

            foreach (DataRow dr in dt.Rows)
            {
                sb.AppendLine();
                sb.Append($"            dic[\"{dr["name"]}\"] = ");
            }

            return sb.ToString();
        }

        public string GetCreateTableSql(string tablename)
        {
            DataTable dt = Select("select sql from sqlite_master where type='table' and name=@vname;",
                new Dictionary<string, object> { { "@vname", tablename } });

            if (dt.Rows.Count == 0)
                return "";

            return dt.Rows[0][0] + "";
        }

        public string GenerateUpdateColumnList(string tablename)
        {
            DataTable dt = Select($"pragma table_info(`{tablename}`);");

            List<string> lst = new List<string>();

            foreach (DataRow dr in dt.Rows)
            {
                // pk column = 0 means it is not a primary key
                if (Convert.ToInt32(dr["pk"]) == 0)
                {
                    lst.Add(dr["name"] + "");
                }
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("List<string> lstUpdateCol = new List<string>();");

            foreach (var l in lst)
            {
                sb.AppendLine();
                sb.Append($"lstUpdateCol.Add(\"{l}\");");
            }

            return sb.ToString();
        }

        public string GenerateParameterDictionaryTable(string tablename)
        {
            string sql = $"select * from `{tablename}` where 1=0;";
            DataTable dt = Select(sql);

            StringBuilder sb = new StringBuilder();
            sb.Append("Dictionary<string, object> dicParam = new Dictionary<string, object>();");

            foreach (DataColumn dc in dt.Columns)
            {
                sb.AppendLine();
                sb.Append($"            dic[\"@{dc.ColumnName}\"] = ");
            }

            return sb.ToString();
        }

        string GenerateClassField(string sql, FieldsOutputType _fieldOutputType)
        {
            var dicColType = GetColumnsDataType(sql);

            StringBuilder sb = new StringBuilder();

            if (_fieldOutputType == FieldsOutputType.PublicProperties || _fieldOutputType == FieldsOutputType.PublicFields)
            {
                foreach (var kv in dicColType)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                    }

                    string datatypestr = GetFieldTypeString(kv.Value);

                    switch (_fieldOutputType)
                    {
                        case FieldsOutputType.PublicProperties:
                            sb.Append($"public {datatypestr} {kv.Key} {{ get; set; }}");
                            break;
                        case FieldsOutputType.PublicFields:
                            sb.Append($"public {datatypestr} {kv.Key} = {GetDefaultValueString(kv.Value)};");
                            break;
                    }
                }
            }
            else if (_fieldOutputType == FieldsOutputType.PrivateFielsPublicProperties)
            {
                bool isfirst = true;

                foreach (var kv in dicColType)
                {
                    string datatypestr = GetFieldTypeString(kv.Value);

                    if (isfirst)
                        isfirst = false;
                    else
                        sb.AppendLine();

                    sb.Append($"{datatypestr} {kv.Key} = {GetDefaultValueString(kv.Value)};");
                }

                sb.AppendLine();

                foreach (var kv in dicColType)
                {
                    string datatypestr = GetFieldTypeString(kv.Value);

                    sb.AppendLine();
                    sb.Append($"public {datatypestr} {GetUpperCaseColName(kv.Key)} {{ get {{ return {kv.Key}; }} set {{ {kv.Key} = value; }} }}");
                }
            }

            return sb.ToString();
        }

        Dictionary<string, Type> GetColumnsDataType(string sql)
        {
            DataTable dt = Select(sql);

            Dictionary<string, Type> dic = new Dictionary<string, Type>();

            foreach (DataColumn dc in dt.Columns)
            {
                dic[dc.ColumnName] = dc.DataType;
            }

            return dic;
        }

        string GetUpperCaseColName(string colname)
        {
            bool toUpperCase = true;

            StringBuilder sb = new StringBuilder();
            foreach (char c in colname)
            {
                if (c == '_')
                {
                    toUpperCase = true;
                    continue;
                }
                if (toUpperCase)
                {
                    sb.Append(Char.ToUpper(c));
                    toUpperCase = false;
                    continue;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        string GetDefaultValueString(Type t)
        {
            if (t == typeof(string))
            {
                return "\"\"";
            }
            else if (t == typeof(bool))
            {
                return "false";
            }
            else if (t == typeof(byte) ||
                t == typeof(sbyte) ||
                t == typeof(short) ||
                t == typeof(ushort) ||
                t == typeof(int) ||
                t == typeof(uint))
            {
                return "0";
            }
            else if (t == typeof(long) ||
                t == typeof(ulong))
            {
                return "0L";
            }
            else if (t == typeof(float))
            {
                return "0F";
            }
            else if (t == typeof(double))
            {
                return "0d";
            }
            else if (t == typeof(decimal))
            {
                return "0m";
            }
            else if (t == typeof(char))
            {
                return "''";
            }
            else if (t == typeof(DateTime))
            {
                return "DateTime.MinValue";
            }
            else if (t == typeof(byte[]))
            {
                return "null";
            }
            else if (t == typeof(Guid))
            {
                return "null";
            }
            else if (t == typeof(TimeSpan))
            {
                return "null";
            }

            throw new Exception($"Unhandled Data Type: {t.ToString()}. Please report this to the development team.");
        }

        string GetFieldTypeString(Type t)
        {
            if (t == typeof(string))
            {
                return "string";
            }
            else if (t == typeof(bool))
            {
                return "bool";
            }
            else if (t == typeof(byte) || t == typeof(sbyte))
            {
                return "byte";
            }
            else if (t == typeof(short) || t == typeof(ushort))
            {
                return "short";
            }
            else if (t == typeof(int) || t == typeof(uint))
            {
                return "int";
            }
            else if (t == typeof(long) || t == typeof(ulong))
            {
                return "long";
            }
            else if (t == typeof(float))
            {
                return "float";
            }
            else if (t == typeof(double))
            {
                return "double";
            }
            else if (t == typeof(decimal))
            {
                return "decimal";
            }
            else if (t == typeof(char))
            {
                return "char";
            }
            else if (t == typeof(DateTime))
            {
                return "DateTime";
            }
            else if (t == typeof(byte[]))
            {
                return "byte[]";
            }
            else if (t == typeof(Guid))
            {
                return "Guid";
            }
            else if (t == typeof(TimeSpan))
            {
                return "TimeSpan";
            }

            throw new Exception($"Unhandled Data Type: {t.ToString()}. Please report this to the development team.");
        }

        #endregion
    }
}