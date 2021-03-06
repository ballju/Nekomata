﻿/* TitleIDConverter.cs
 * This class converts one title id from one service to another.
 * 
 * Copyright (c) 2018 MAL Updater OS X Group, a division of Moy IT Solutions
 * Licensed under MIT License
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using RestSharp;
using Newtonsoft.Json;
using System.Data.SQLite;
using Newtonsoft.Json.Linq;

namespace NekomataCore
{
    public enum Service
    {
        Kitsu = 1,
        AniList = 2
    }

    public class TitleIDConverter
    {
        RestClient raclient;
        RestClient rkclient;
        SQLiteConnection sqlitecon;
        public bool sqlliteinitalized;

        public TitleIDConverter()
        {
            raclient = new RestClient("https://graphql.anilist.co");
            rkclient = new RestClient("https://kitsu.io/api/edge");
            this.initalizeDatabase();
        }
        public int GetMALIDFromKitsuID(int kitsuid, EntryType type)
        {
            int titleid = this.RetreiveSavedMALIDFromServiceID(Service.Kitsu, kitsuid, type);
            if (titleid > -1)
            {
                return titleid;
            }
            String typestr = "";
            switch (type)
            {
                case EntryType.Anime:
                    typestr = "anime";
                    break;
                case EntryType.Manga:
                    typestr = "manga";
                    break;
                default:
                    break;
            }

            String filterstr = "myanimelist/" + typestr;
   
            RestRequest request = new RestRequest( "/" + typestr + "/" + kitsuid.ToString() + "?include=mappings&fields[anime]=id", Method.GET);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Accept", "application/vnd.api+json");

            IRestResponse response = rkclient.Execute(request);
            Thread.Sleep(1000);
            if (response.StatusCode.GetHashCode() == 200)
            {
                Dictionary<string, object> jsonData = JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Content);
                if (jsonData.ContainsKey("included"))
                {
                    List<Dictionary<string, object>> included = ((JArray)jsonData["included"]).ToObject<List<Dictionary<string, object>>>();
                    foreach (Dictionary<string, object> map in included)
                    {
                        Dictionary<string, object> attr = JObjectToDictionary((JObject)map["attributes"]);
                        if (String.Equals(((String)attr["externalSite"]), "myanimelist/" + typestr, StringComparison.OrdinalIgnoreCase))
                        {
                            int malid = int.Parse((string)attr["externalId"]);
                            this.SaveIDtoDatabase(Service.Kitsu, malid, kitsuid, type);
                            return malid;
                        }
                    }
                }
                return -1;
            }
            else
            {
                return -1;
            }

        }

        public int GetMALIDFromAniListID(int anilistid, EntryType type)
        {
            int titleid = this.RetreiveSavedMALIDFromServiceID(Service.AniList, anilistid, type);
            if (titleid > -1)
            {
                return titleid;
            }
            String typestr = "";
            switch (type)
            {
                case EntryType.Anime:
                    typestr = "ANIME";
                    break;
                case EntryType.Manga:
                    typestr = "MANGA";
                    break;
                default:
                    break;
            }

            RestRequest request = new RestRequest("/", Method.POST);
            request.RequestFormat = DataFormat.Json;
            GraphQLQuery gquery = new GraphQLQuery();
            gquery.query = "query ($id: Int!, $type: MediaType) {\n  Media(id: $id, type: $type) {\n    id\n    idMal\n  }\n}";
            gquery.variables = new Dictionary<string, object> { { "id" , anilistid.ToString() }, { "type", typestr } };
            request.AddJsonBody(gquery);
            IRestResponse response = raclient.Execute(request);
            Thread.Sleep(2000);
            if (response.StatusCode.GetHashCode() == 200)
            {
                Dictionary<string, object> jsonData = JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Content);
                Dictionary<string, object> data = JObjectToDictionary((JObject)jsonData["data"]);
                Dictionary<string, object> media = JObjectToDictionary((JObject)data["Media"]);
                int malid = !object.ReferenceEquals(null, media["idMal"]) ? Convert.ToInt32((long)media["idMal"]) : -1;
                if (malid > 0)
                {
                    this.SaveIDtoDatabase(Service.AniList, malid, anilistid, type);
                    Thread.Sleep(2000);
                    return malid;
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                return -1;
            }

        }

        public int RetreiveSavedMALIDFromServiceID(Service listService, int titleid, EntryType type)
        {
            String sql = "";
            int mediatype = type == EntryType.Anime ? 0 : 1;
            switch (listService)
            {
                case Service.AniList:
                    sql = "SELECT malid FROM titleids WHERE anilist_id = " + titleid.ToString() + " AND mediatype = " + mediatype.GetHashCode();
                    break;
                case Service.Kitsu:
                    sql = "SELECT malid FROM titleids WHERE kitsu_id = " + titleid.ToString() + " AND mediatype = " + mediatype.GetHashCode();
                    break;
                default:
                    break;
            }
            SQLiteCommand cmd = new SQLiteCommand(sql, sqlitecon);
            SQLiteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                return (int)reader[@"malid"];
            }

            return -1;
        }

        public void SaveIDtoDatabase(Service listservice, int malid, int servicetitleid, EntryType type)
        {
           if (this.CheckIfEntryExists(malid,type))
           {
               // Update entry
               String sql = "";
               int mediatype = type == EntryType.Anime ? 0 : 1;
               switch (listservice)
               {
                    case Service.AniList:
                        sql = "UPDATE titleids SET anilist_id = " + servicetitleid.ToString() + " WHERE mediatype = " + mediatype.GetHashCode() + " AND malid = " + malid.ToString();
                        break;
                    case Service.Kitsu:
                        sql = "UPDATE titleids SET kitsu_id = " + servicetitleid.ToString() + " WHERE mediatype = " + mediatype.GetHashCode() + " AND malid = " + malid.ToString();
                        break;
                    default:
                        return;
               }
               SQLiteCommand cmd = new SQLiteCommand(sql, sqlitecon);
               cmd.ExecuteNonQuery();
           }
           else
            {
                // Insert entry
                this.InsertIDtoDatabase(listservice, malid, servicetitleid, type);
            }

        }

        private void InsertIDtoDatabase(Service listservice, int malid, int servicetitleid, EntryType type)
        {
            String sql = "";
            int mediatype = type == EntryType.Anime ? 0 : 1;
            switch (listservice)
            {
                case Service.AniList:
                    sql = "INSERT INTO titleids (malid,anilist_id,mediatype) VALUES (" + malid.ToString() + "," + servicetitleid.ToString() + "," + type.GetHashCode() + ")";
                    break;
                case Service.Kitsu:
                    sql = "INSERT INTO titleids (malid,kitsu_id,mediatype) VALUES (" + malid.ToString() + "," + servicetitleid.ToString() + "," + type.GetHashCode() + ")";
                    break;
                default:
                    return;
            }
            SQLiteCommand cmd = new SQLiteCommand(sql, sqlitecon);
            cmd.ExecuteNonQuery();

        }

        private bool CheckIfEntryExists(int malid, EntryType type)
        {
            int mediatype = type == EntryType.Anime ? 0 : 1;
            String sql = "SELECT malid FROM titleids WHERE malid = " + malid.ToString() + " AND mediatype = " + mediatype.GetHashCode();
         
            SQLiteCommand cmd = new SQLiteCommand(sql, sqlitecon);
            SQLiteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                return true;
            }

            return false;
        }

        private void initalizeDatabase()
        {
            try
            {
                string directory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/Nekomata";
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                if (File.Exists(directory + "Nekomata.sqlite"))
                {
                    sqlitecon = new SQLiteConnection("Data Source=" + directory + "\\Nekomata.sqlite;Version=3;");
                    sqlitecon.Open();
                }
                else
                {
                    // Create database and tables
                    SQLiteConnection.CreateFile(directory + "\\Nekomata.sqlite");
                    sqlitecon = new SQLiteConnection("Data Source=" + directory + "\\Nekomata.sqlite;Version=3;");
                    sqlitecon.Open();
                    SQLiteCommand createtable = new SQLiteCommand("CREATE TABLE titleids (anidb_id INT, anilist_id INT, kitsu_id INT, malid INT, animeplanet_id VARCHAR(50), mediatype INT)");
                    createtable.Connection = this.sqlitecon;
                    createtable.ExecuteNonQuery();
                }
                sqlliteinitalized = true;
            }
            catch (Exception e)
            {
                sqlliteinitalized = false;
            }
        }

        private Dictionary<string, object> JObjectToDictionary(JObject jobject)
        {
            return jobject.ToObject<Dictionary<string, object>>();
        }
    }
}
