using CapacitorScanner.Core.Model;
using CapacitorScanner.Core.Model.LocalDb;
using Dapper;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CapacitorScanner.Core.Services
{
    public class BinLocalDbService
    {
        private string _connectionString;
        private ConfigService _config;
        public BinLocalDbService(ConfigService config)
        {
            _config = config;
            var builder = new SqliteConnectionStringBuilder();
            builder.DataSource = config.Config.dbpath;
            _connectionString = builder.ToString();
        }

        private async Task<SqliteConnection> GetConn()
        {
            var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            return conn;
        }
        string HashPassword(string password)
        {
            byte[] key = Encoding.UTF8.GetBytes("asjdlnkcnalnaehneuvnq1uf9q91fvbcibnckncknkzxn=13fkanp33922acnae");
            using (var hm = new HMACSHA512(key))
            {
                byte[] enc = Encoding.UTF8.GetBytes(password);
                byte[] buffer = hm.ComputeHash(enc);
                return Convert.ToBase64String(buffer);
            }
        }

        public async Task Initialization()
        {

            try
            {
                if (!File.Exists(_config.Config.dbpath))
                {
                    var fs = File.Create(_config.Config.dbpath);
                    fs.Close();
                    string file = await File.ReadAllTextAsync("scanner-init.sql");
                    using (var con = await GetConn())
                    {
                        await con.ExecuteAsync(file);
                        string query = "insert into login(username,password) values(@username,@password)";
                        await con.ExecuteAsync(query, new { username = "admin", password = HashPassword("123") });
                    }
                }
            }
            catch (Exception ex)
            {
                File.Delete(_config.Config.dbpath);
                Console.WriteLine(ex.Message);
            }
        }
        public async Task<bool> UpdatePasswordLogin(string username,string oldpassword,string newpassword)
        {
            var hashOldPassword = HashPassword(oldpassword);
            var login = await Login(new LoginModel() { username = username, password =hashOldPassword });
            if (login is null)
                return false;
            using (var con = await GetConn())
            {
                string query = "update login set password=@password where username=@username";
                await con.ExecuteAsync(query, new { username = username, password = HashPassword(newpassword) });
            }
            return true;
        }
        public async Task CreateTransaction(ScrapTransactionModel transaction)
        {
            var check = await GetScrapTransaction(transaction.Code);
            if (check.Any() || string.IsNullOrEmpty(transaction.Code))
                return;
            using (var con = await GetConn())
            {
                   
                await con.ExecuteAsync(@"INSERT INTO ScrapTransaction (
                    transaction_date,
                    login_date,
                    send_date,  
                    badgeno,
                    container,
                    bin,
                    status,
                    host,
                    weightresult,
                    activity,
                    lastbadgeno,
                    realweight,
                    prevweight,
                    code
                ) VALUES (
                    @TransactionDate,
                    @LoginDate,
                    @SendDate,  
                    @Badgeno,
                    @Container,
                    @Bin,
                    @Status,
                    @Host,
                    @WeightResult,
                    @Activity,
                    @LastBadgeno,
                    @RealWeight,
                    @PrevWeight,
                    @Code
                );", transaction);
            }
        }

        public async Task<LoginModel?> Login(LoginModel login)
        {
            using (var con = await GetConn())
            {
                string query = $"Select id,username,password from login where username=@username and password=@password";
                var res = await con.QueryAsync<LoginModel>(query, login);
                return res.FirstOrDefault();
            }
        }
        public async Task<string> GetHostname(string bin)
        {
            using (var con = await GetConn())
            {
                string query = $"Select hostname from binhost where bin=@bin";
                var res = await con.ExecuteScalarAsync<string>(query, new { bin });
                return res!;
            }
        }
        public async Task<BinLocalModel?> GetBin(string bin)
        {
            using (var con = await GetConn())
            {
                string query = $"Select bin,weight,binweight,lastfrombinname,lastbadgeno,maxweight,wastetype,hostname,status from binhost where bin=@bin";
                var res = await con.QueryFirstOrDefaultAsync<BinLocalModel>(query, new { bin });
                return res;
            }
        }
        public async Task<IEnumerable<BinLocalModel>> GetBin()
        {
            using (var con = await GetConn())
            {
                string query = $"Select bin,weight,binweight,lastfrombinname,lastbadgeno,maxweight,wastetype,hostname,status from binhost";
                var res = await con.QueryAsync<BinLocalModel>(query);
                return res;
            }
        }
        public async Task UpdateBin(BinLocalModel bin)
        {
            using (var con = await GetConn())
            {
                string query = $"Update binhost set weight=@weight,lastfrombinname=@lastfrombinname,lastbadgeno=@lastbadgeno,binweight=@binweight,wastetype=@wastetype,hostname=@hostname where bin=@bin";
                await con.ExecuteAsync(query, bin);
            }

        }
        public async Task InsertBinHost(BinLocalModel bin)
        {
            using (var con = await GetConn())
            {
                string query = $"Insert into binhost(bin,weight,binweight,maxweight,hostname,status,wastetype,lastfrombinname,lastbadgeno) values(@bin,@weight,@binweight,@maxweight,@hostname,@status,@wastetype,@lastfrombinname,@lastbadgeno)";
                await con.ExecuteAsync(query, bin);
            }
        }
        public async Task<IEnumerable<ScrapTransactionModel>> GetFailedORReadyScrapTransaction()
        {
            using (var con = await GetConn())
            {
                string query = $"Select id,transaction_date as TransactionDate,send_date as SendDate,login_date as LoginDate,badgeno as BadgeNo,container as Container,bin as Bin,status as Status,host as Host,weightresult as WeightResult,activity as Activity,lastbadgeno as LastBadgeNo,realweight as RealWeight,prevweight as PrevWeight,code as Code from scraptransaction" +
                    $" where status IN ('FAILED','READY')  order by datetime(transaction_date) asc";

                return await con.QueryAsync<ScrapTransactionModel>(query);
            }
        }
        public async Task<IEnumerable<ScrapTransactionModel>> GetScrapTransaction(string code)
        {
            using (var con = await GetConn())
            {
                string query = $"Select id,transaction_date as TransactionDate,send_date as SendDate,login_date as LoginDate,badgeno as BadgeNo,container as Container,bin as Bin,status as Status,host as Host,weightresult as WeightResult,activity as Activity,lastbadgeno as LastBadgeNo,realweight as RealWeight,prevweight as PrevWeight,code as Code from scraptransaction" +
                    $" where code=@code  order by datetime(transaction_date) asc";

                return await con.QueryAsync<ScrapTransactionModel>(query, new { code });
            }
        }
        public async Task UpdateStatus(string status, int id)
        {
            using (var con = await GetConn())
            {
                string query = $"Update scraptransaction set status=@status where id=@id";
                await con.ExecuteAsync(query, new { status, id });
            }
        }
        public async Task UpdateStatusBin(string status, string bin)
        {
            using (var con = await GetConn())
            {
                string query = $"Update binhost set status=@status where bin=@bin";
                await con.ExecuteAsync(query, new { status, bin });
            }
        }
        public async Task DeleteBin(string bin)
        {
            using (var con = await GetConn())
            {
                string query = $"delete from binhost where bin=@bin";
                await con.ExecuteAsync(query, new { bin });
            }
        }
        public async Task<IEnumerable<StationInfoLocalModel>> GetStationInfoLocal(string? property = null)
        {
            using (var con = await GetConn())
            {
                string query = $"Select id,property,datavalue from station where property like @property";
                return await con.QueryAsync<StationInfoLocalModel>(query, new { property = $"%{property}%" });
            }
        }
        public async Task AddOrUpdateStationInfoLocal(StationInfoLocalModel stationInfoLocal)
        {
            var stationData = await GetStationInfoLocal(stationInfoLocal.property);
            using (var con = await GetConn())
            {
                string query = "";
                if (stationData.Any())
                {
                    query = $"Update station set datavalue=@datavalue where property=@property";
                }
                else
                {
                    query = $"Insert into station(property,datavalue) values(@property,@datavalue)";
                }
                await con.ExecuteAsync(query, stationInfoLocal);
            }
        }
        public async Task InsertEmployee(EmployeeLocalModel employeeLocalModel)
        {
            var check = await GetEmployee(employeeLocalModel.badgeno);
            if (check.Any())
                return;
            using (var con = await GetConn())
            {
                await con.ExecuteAsync($"Insert into employee(employeename,badgeno,registerdate,dispose,collection) values(@employeename,@badgeno,@registerdate,@dispose,@collection)", employeeLocalModel);
            }
        }
        public async Task UpdateEmployee(EmployeeLocalModel employeeLocalModel)
        {
            var check = await GetEmployee(employeeLocalModel.badgeno);
            if (!check.Any())
                return;
            using (var con = await GetConn())
            {
                await con.ExecuteAsync($"update employee set dispose=@dispose,collection=@collection where badgeno=@badgeno", employeeLocalModel);
            }
        }
        public async Task<IEnumerable<EmployeeLocalModel>> GetEmployee(string? badgeNo = null)
        {
            using (var con = await GetConn())
            {
                string query = $"Select id,employeename,badgeno,registerdate,dispose,collection from employee where badgeno=@badgeNo";
                return await con.QueryAsync<EmployeeLocalModel>(query, new { badgeNo });
            }
        }
        public async Task<ContainerBinLocalModel?> GetContainerBinLocal(string? name = null)
        {
            using (var con = await GetConn())
            {
                string query = $"Select activity,name,description,scrapitem_name,scraptype_name,weight,capacity,weightresult,weightsystem,wastestation_name,department_name,logindate,doorstatus,lastfrombinname,url,scrapgroup_name,lastbadgeno from containerbin where name=@name";
                return await con.QueryFirstOrDefaultAsync<ContainerBinLocalModel>(query, new { name });
            }
        }
        public async Task InsertContainerBinLocal(ContainerBinLocalModel model)
        {
            var containerLocal = await GetContainerBinLocal(model.name);
            if (containerLocal is not null)
                return;
            using (var con = await GetConn())
            {
                string query = $"Insert into containerbin(activity,name,description,scrapitem_name,scraptype_name,weight,capacity,weightresult,weightsystem,wastestation_name,department_name,logindate,doorstatus,lastfrombinname,url,scrapgroup_name,lastbadgeno) " +
                    $"values(@activity,@name,@description,@scrapitem_name,@scraptype_name,@weight,@capacity,@weightresult,@weightsystem,@wastestation_name,@department_name,@logindate,@doorstatus,@lastfrombinname,@url,@scrapgroup_name,@lastbadgeno)";
                await con.ExecuteAsync(query, model);
            }
        }
        public async Task SetAppData(string property, object? value)
        {
            using (var con = await GetConn())
            {
                string query = $"Select id from appdata where property=@property";
                var res = await con.QueryFirstOrDefaultAsync<int?>(query, new { property });
                if (res is null)
                {
                    query = $"Insert into appdata(property,datavalue) values(@property,@datavalue)";
                    await con.ExecuteAsync(query, new { property, datavalue = value is not null ? JsonSerializer.Serialize(value) : null });
                }
                else
                {
                    query = $"Update appdata set datavalue=@datavalue where property=@property";
                    await con.ExecuteAsync(query, new { property, datavalue = value is not null ? JsonSerializer.Serialize(value) : null });
                }
            }
        }
        public async Task<T?> GetAppData<T>(string property)
        {
            using (var con = await GetConn())
            {
                try
                {
                    string query = $"Select datavalue from appdata where property=@property";
                    var res = await con.QueryFirstOrDefaultAsync<string?>(query, new { property });
                    if (res is null)
                        return default;
                    var parsed = JsonSerializer.Deserialize<T>(res) ?? default;
                    return parsed;
                }
                catch(Exception e)
                {
                    Console.WriteLine(e.Message+ " " +e.InnerException?.Message);
                    return default;
                }
            }
        }
    }
}
