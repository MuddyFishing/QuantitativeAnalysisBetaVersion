﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace QuantitativeAnalysis.DataAccess.Infrastructure
{
    public class RedisReader
    {
        public string HGet(string key,string field,int dbId=0)
        {
            var db = RedisClientSingleton.Instance.GetDatabase(dbId);

            return db.HashGet(key, field);
        }

        public HashEntry[] HGetAll(string key,int dbId = 0)
        {
            var db = RedisClientSingleton.Instance.GetDatabase(dbId);
            return db.HashGetAll(key);
        }
    }
}
