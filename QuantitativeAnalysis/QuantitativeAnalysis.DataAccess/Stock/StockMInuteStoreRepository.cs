﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuantitativeAnalysis.Model;
using QuantitativeAnalysis.DataAccess.Infrastructure;
using System.Configuration;
using System.Data;
using QuantitativeAnalysis.Utilities;
using NLog;
using static QuantitativeAnalysis.Utilities.DateTimeExtension;
namespace QuantitativeAnalysis.DataAccess.Stock
{
    public class StockMinuteStoreRepository
    {
        private TransactionDateTimeRepository dateTimeRepo;
        private RedisReader redisReader;
        private SqlServerWriter sqlWriter;
        private SqlServerReader sqlReader;
        private RedisWriter redisWriter;
        private IDataSource dataSource;
        private Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public StockMinuteStoreRepository(ConnectionType type, IDataSource ds)
        {
            dateTimeRepo = new TransactionDateTimeRepository(type);
            sqlWriter = new SqlServerWriter(type);
            sqlReader = new SqlServerReader(type);
            redisReader = new RedisReader();
            redisWriter = new RedisWriter();
            dataSource = ds;
        }

        private void BulkLoadStockMinuteToSqlFromSource(string code, DateTime currentTime,DataTable dataTable)
        {
            IdentifyOrCreateDBAndTable(currentTime);
            var latestTime = GetLatestTimeFromSql(code, currentTime);
            latestTime = latestTime == default(DateTime) ? new DateTime(currentTime.Year, 1, 1) : latestTime.AddMinutes(1);
            var endTime = GetEndTime(currentTime);
            if (latestTime < endTime)
            {
                //清洗NaN数据
                for (int i = dataTable.Rows.Count - 1; i >= 0; i--)
                {
                    if (double.IsNaN(Convert.ToDouble(dataTable.Rows[i]["open"])) == true)
                    {
                        dataTable.Rows[i]["open"] = DBNull.Value;
                    }
                    if (double.IsNaN(Convert.ToDouble(dataTable.Rows[i]["high"])) == true)
                    {
                        dataTable.Rows[i]["high"] = DBNull.Value;
                    }
                    if (double.IsNaN(Convert.ToDouble(dataTable.Rows[i]["low"])) == true)
                    {
                        dataTable.Rows[i]["low"] = DBNull.Value;
                    }
                    if (double.IsNaN(Convert.ToDouble(dataTable.Rows[i]["close"])) == true)
                    {
                        dataTable.Rows[i]["close"] = DBNull.Value;
                    }
                }
                WriteToSql(dataTable);
            }
        }


        private void WriteToSql(DataTable dataTable)
        {
            Dictionary<DateTime, DataTable> monthData = SplitDataTableMonthly(dataTable);
            foreach (var item in monthData)
            {
                IdentifyOrCreateDBAndTable(item.Key);
                sqlWriter.InsertBulk(item.Value, string.Format("[StockMinuteTransaction{0}].dbo.[Transaction{1}]", item.Key.Year, item.Key.ToString("yyyy-MM")));
            }
        }

        private Dictionary<DateTime, DataTable> SplitDataTableMonthly(DataTable dataTable)
        {
            var monthData = new Dictionary<DateTime, DataTable>();
            foreach (DataRow r in dataTable.Rows)
            {
                var date = r["DateTime"].ToString().ConvertTo<DateTime>();
                var key = new DateTime(date.Year, date.Month, 1);
                if (!monthData.ContainsKey(key))
                {
                    var dt = dataTable.Clone();
                    dt.ImportRow(r);
                    monthData.Add(key, dt);
                }
                else
                    monthData[key].ImportRow(r);
            }
            return monthData;
        }

        /// <summary>
        /// 计算当前日期下的最后交易时间
        /// </summary>
        /// <param name="currentTime"></param>
        /// <returns></returns>
        private DateTime GetEndTime(DateTime currentTime)
        {
            if (currentTime.Year < DateTime.Now.Year)
                return dateTimeRepo.GetLastTransactionDate(currentTime, DateLevel.Year).AddHours(15).AddMinutes(0);
            var date = new DateTime(currentTime.Year, DateTime.Now.Month, DateTime.Now.Day - 1, 15, 0, 0);
            date = DateUtils.PreviousTradeDay(date);
            return new DateTime(date.Year, date.Month, date.Day, 15, 0, 0);
        }

        /// <summary>
        /// 找到当年SQL数据对应标的的最后一条数据的时间。为了插入新的数据做准备。
        /// </summary>
        /// <param name="code"></param>
        /// <param name="currentTime"></param>
        /// <returns></returns>
        private DateTime GetLatestTimeFromSql(string code, DateTime currentTime)
        {
            DateTime latest = default(DateTime);
            var sqlStr = string.Format(@"declare @date date,@tb_name nvarchar(60), @index int,@latest_date datetime,@sqlStr nvarchar(300),@tem_date datetime
set @date ='{0}-01-01'
set @index =1
while @index <=12
begin
	set @tb_name='[StockMinuteTransaction'+datename(year,@date)+'].dbo.[Transaction'+ datename(year,@date)+'-'+datename(month,@date)+']'
	set @sqlStr ='select @tem_date=max([DateTime]) from '+@tb_name+' where code=''{1}'''
	if object_id(@tb_name) is not null
	begin
		exec sp_executesql @sqlStr,N'@tem_date datetime output',@tem_date output;
		if @tem_date is not null
		begin
			set @latest_date=@tem_date
		end
	end
	set @date = dateadd(month,1,@date)
	set @index=@index+1
end
select @latest_date", currentTime.Year, code.ToUpper());
            latest = sqlReader.ExecuteScriptScalar<DateTime>(sqlStr);
            return latest;
        }

        private void IdentifyOrCreateDBAndTable(DateTime dateTime)
        {
            var sqlLocation = ConfigurationManager.AppSettings["SqlServerLocation"];
            var sqlScript = string.Format(@"USE [master]
if db_id('StockMinuteTransaction{0}') is null
begin
CREATE DATABASE [StockMinuteTransaction{0}]
 CONTAINMENT = NONE
 ON  PRIMARY 
( NAME = N'StockMinuteTransaction{0}', FILENAME = N'{1}\StockMinuteTransaction{0}.mdf' , SIZE = 5120KB , MAXSIZE = UNLIMITED, FILEGROWTH = 1024KB )
 LOG ON 
( NAME = N'StockMinuteTransaction{0}_log', FILENAME = N'{1}\StockMinuteTransaction{0}_log.ldf' , SIZE = 2048KB , MAXSIZE = 2048GB , FILEGROWTH = 10%)
ALTER DATABASE [StockMinuteTransaction{0}] SET COMPATIBILITY_LEVEL = 120
IF (1 = FULLTEXTSERVICEPROPERTY('IsFullTextInstalled'))
begin
EXEC [StockMinuteTransaction{0}].[dbo].[sp_fulltext_database] @action = 'enable'
end
end
go
if object_id('[StockMinuteTransaction{0}].[dbo].[Transaction{2}]') is null
begin
CREATE TABLE [StockMinuteTransaction{0}].[dbo].[Transaction{2}](
	[Code] [varchar](20) NOT NULL,
	[DateTime] [datetime] NOT NULL,
	[open] [decimal](12, 4) NULL,
	[high] [decimal](12, 4) NULL,
	[low] [decimal](12, 4) NULL,
	[close] [decimal](12, 4) NULL,
	[volume] [decimal](20, 4) NULL,
	[amount] [decimal](20, 4) NULL,
	[UpdatedDateTime] [datetime] NULL,
 CONSTRAINT [PK_Transaction{2}] PRIMARY KEY CLUSTERED 
(
	[Code] ASC,
	[DateTime] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
SET ANSI_PADDING OFF
ALTER TABLE [StockMinuteTransaction{0}].[dbo].[Transaction{2}] ADD  CONSTRAINT [DF_Transaction{2}_UpdatedDateTime]  DEFAULT (getdate()) FOR [UpdatedDateTime]
end ", dateTime.Year, sqlLocation, dateTime.ToString("yyyy-MM"));
            sqlWriter.ExecuteSqlScript(sqlScript);
        }

        private bool ExistInSqlServer(string code, DateTime date)
        {
            var sqlScript = string.Format(@"use master
if db_id('StockMinuteTransaction{0}') is not null
begin
	if object_id('[StockMinuteTransaction{0}].dbo.[Transaction{0}-{1}]') is not null
	begin
		select 1 from [StockMinuteTransaction{0}].dbo.[Transaction{0}-{1}] where rtrim(Code)='{2}'
	end
end
else
begin
select 0
end ", date.Year, date.ToString("MM"), code);
            var res = sqlReader.ExecuteScriptScalar<int>(sqlScript);
            return res > default(int);
        }
    }
}
