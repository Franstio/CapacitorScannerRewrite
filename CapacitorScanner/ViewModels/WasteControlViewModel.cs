using Avalonia.Data;
using CapacitorScanner.Core.API.Model;
using CapacitorScanner.Core.Model;
using CapacitorScanner.Core.Services;
using CapacitorScanner.Messages;
using CapacitorScanner.Core.Model.PIDSG;
using CapacitorScanner.Services;
using CapacitorScanner.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MsBox.Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Web;
using CapacitorScanner.Core.Model.LocalDb;

namespace CapacitorScanner.ViewModels
{
    public partial class WasteControlViewModel: ViewModelBase
    {
        private readonly PIDSGService Service;
        private readonly ConfigService ConfigService;   
        private readonly BinLocalDbService DbService;
        private readonly DialogService dialogService;
        private HttpClient httpClient;
        private enum TransactionType
        {
            Dispose,
            Collection,
            Manual
        }

        private TransactionType? transactionType = null;

        [ObservableProperty]
        private UserModel? user = null;

        [ObservableProperty]
        private string scan = string.Empty;

        [ObservableProperty]
        private BinActivityModel? openBin = null;

        [ObservableProperty]
        private ContainerBinModel? container = null;
        
        [ObservableProperty]
        private bool isAuto = true;
        
        [ObservableProperty]
        private string message= "Scan Badge ID";
        
        private DateTime loginDate = DateTime.Now;
        
        public WasteControlViewModel(PIDSGService service,BinLocalDbService _db,ConfigService config,DialogService dialogService,HttpClient client) 
        {
            Service = service;
            DbService = _db; 
            ConfigService = config;
            this.httpClient = client;
            this.dialogService = dialogService;
        }
        private System.Threading.Timer? timer = null;
        public ObservableCollection<BinModel> Bins { get; set; } = [new BinModel("test","test",1,23)];
        
        [RelayCommand]
        public void LoadBins()
        {
            if (timer is not null)
                timer.Dispose();
            timer = new System.Threading.Timer(async (obj) =>
            {
                try
                {
                    if (Container is not null)
                        return;
//                    bool resultServer = await LoadBinFromPidsg();
  //                  if (!resultServer) 
                        await LoadBinFromLocal();

                }
                catch { }
            },null,0,3000);

        }
        async Task<bool> LoadBinFromPidsg()
        {
            var data = await Service.GetBins();
            if (data is null)
                return false;
            Bins.Clear();
            var _data = data.Select(x => new BinModel(x.name, x.scraptype_name, x.weightresult, x.capacity)).OrderBy(x => x.Name);
            foreach (var item in _data)
            {
                Bins.Add(item);
            }
            return true;
        }
        async Task<bool> LoadBinFromLocal()
        {
            var data = await DbService.GetBin();
            if (!data.Any())
                return false;
            Bins.Clear();
            var _data = data.Select(x => new BinModel(x.bin, x.wastetype, x.weight, x.maxweight)).OrderBy(x => x.Name);
            foreach (var item in _data)
            {
                Bins.Add(item);
            }
            return true;
        }
        void ResetStateInput(string message = "Scan Badge ID")
        {
            Message = message;
            User = null;
            Container = null;
            OpenBin = null;
            transactionType = null;
        }

        public async Task<ContainerBinModel?> LoadContainerBin(string binName)
        {
            var data = (await Service.GetBins(binName)) ;
            Console.WriteLine(JsonSerializer.Serialize( data));
            var ret = Container = data?.FirstOrDefault() ?? (await DbService.GetContainerBinLocal(Scan))?.ToApiModel();

            return ret;
        }
        async Task HandleLogin()
        {
            User = await Service.LoginUser(Scan);
            loginDate = DateTime.Now;
            await DbService.SetAppData(nameof(loginDate), loginDate);
            if (User is null)
            {
                var localUsr = (await DbService.GetEmployee(Scan)).FirstOrDefault();
                if (localUsr != null)
                {
                    User = new UserModel(localUsr.employeename, loginDate.ToString("yyyy-MM-dd"), localUsr.badgeno);
                }
            }
            if (User is not null)
            {
                await DbService.InsertEmployee(new EmployeeLocalModel()
                {
                    badgeno = User.badgeno,
                    employeename = User.employeename,
                    registerdate = loginDate.ToString("yyyy-MM-dd")
                });
                Message = "Scan QR Code Sampah";
            }
            else
            {
                await dialogService.ShowMessageAsync("Scan Failed", "User Not Found");
            }
        }
        async Task ContainerScan()
        {
            var container = await LoadContainerBin(Scan);
            if (container is null)
            {
                await dialogService.ShowMessageAsync("Scan Failed","Container Not Found");
                return;
            }
            if (IsAuto)
                await ContainerAuto(container);
            else
                await ContainerManual();
        }
        private async Task<BinActivityModel?> LocalAutoProcessBinActivity(ContainerBinModel container)
        {
            var localContainer = await DbService.GetContainerBinLocal(container.name);
            if (localContainer is null)
                return null;
            if (localContainer.activity == "Collection")
            {
                return new BinActivityModel()
                {
                    activity = 2,
                    openbinname = Scan,
                    status = "OK"
                };
            }
            else
            {
                var bins = Bins.Where(x => x.WasteType == localContainer.scraptype_name && x.Percentage <= 80).FirstOrDefault();
                return bins is null ? null : new BinActivityModel()
                {
                    activity = 1,
                    openbinname = bins.Name,
                    status = "OK"
                };
            }
        }
        async Task ContainerAuto(ContainerBinModel containerBin)
        {
            if (User is null)
                throw new Exception("User haven't login yet");
            int[] activity = [1, 2];
            var bin = await Service.AutoProcessBinActivity(User.badgeno, Scan) ;
            var employee = (await DbService.GetEmployee(User.badgeno)).First();
            if (bin is not null)
            {
                if (bin?.activity == 1)
                    employee.dispose = true;
                else if (bin?.activity == 2)
                    employee.collection = true;
                await DbService.UpdateEmployee(employee);
            }
            else
            {
                bin = await LocalAutoProcessBinActivity(containerBin);
                if ((bin?.activity == 1 && !employee.dispose) ||
                    (bin?.activity == 2 && !employee.collection))
                {
                    await dialogService.ShowMessageAsync("Unauthorized Access", $"Employee Unauthorized to {(bin?.activity == 1 ? $"Dispose" : "Collection")}" );
                    return;
                }
            }
            if (bin?.openbinname == "nothing")
            {

                OpenBin = null;
                await dialogService.ShowMessageAsync("Scan Failed", bin?.status ?? "-");
                return;
            }
            OpenBin = bin;
            if (bin is null || OpenBin is null) return;
            var dataBin = await DbService.GetBin(OpenBin.openbinname);
            if (dataBin is null)
            {
                OpenBin = null;
                await dialogService.ShowMessageAsync("Scan Failed", $"User is not register, please check station in server");
                return;
            }
            dataBin!.lastbadgeno = User.badgeno;
            if (activity.Contains(bin.activity) && bin.openbinname.ToLower() != "nothing")
            { 
                if (dataBin.prevweight is null || dataBin.prevweight == 0)
                    dataBin.prevweight = dataBin.weight;
                else
                {
                    decimal diffCalc = dataBin.prevweight == 0 ?
                            Math.Abs(((dataBin.prevweight.Value! - dataBin.weight) / Math.Abs(dataBin.weight)) * 100) :
                            Math.Abs(((dataBin.weight - dataBin.prevweight!.Value!) / Math.Abs(dataBin.prevweight.Value!)) * 100);
                    Console.WriteLine(diffCalc);
                    dataBin.prevweight = diffCalc > 4 ? dataBin.prevweight  : dataBin.weight;
                }
                await DbService.UpdateBin(dataBin);
                await DbService.UpdatePrevWeight(dataBin.bin, dataBin.prevweight.Value);
                await DbService.UpdateStatusBin(bin.activity == 1 ? "Dispose" : "Collection", bin.openbinname);
                var localContainer = containerBin.ToLocalModel();
                localContainer.activity = bin.activity == 1 ? "Dispose" : "Collection";
                await DbService.InsertContainerBinLocal(localContainer);
                transactionType = bin.activity == 1 ? TransactionType.Dispose : TransactionType.Collection;
                await DbService.SetAppData(nameof(transactionType), transactionType);
                Message = $"Verification {transactionType.ToString()}\nScan QR Code Container bin";
            }
            else
            {
                Scan = string.Empty;
                await dialogService.ShowMessageAsync("Bin Error", bin?.activity == 0 ? $"Bin Overload" : bin?.status ?? "");
                ResetStateInput();
            }

        }
        async Task ContainerManual()
        {

            Message = "Waste process\n Pilih Waste bin";
            transactionType = TransactionType.Manual;
            await DbService.SetAppData(nameof(transactionType), transactionType);
        }
        async Task<bool> SendBinVerif(string bin)
        {
            string[] urls = ["https", "http"];
            Task<bool>[] tasks = new Task<bool>[urls.Length];
            bool result = false;
            CancellationTokenSource tokenCancel = new CancellationTokenSource();
            try
            {
                tokenCancel.Token.ThrowIfCancellationRequested();
                tokenCancel.CancelAfter(TimeSpan.FromSeconds(7));
                do
                {
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        string url = urls[i];
                        tasks[i] = Task.Run(async () =>
                        {
                            try
                            {
                                string binhost = await DbService.GetHostname(bin);
                                string token = $"root:00000000";
                                string base64token = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
                                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, $"{url}://{binhost}/verifikasi?verifikasi=1");
                                req.Headers.TryAddWithoutValidation("Authorization", $"Basic {base64token}");

                                var res = await httpClient.SendAsync(req);
                                res.EnsureSuccessStatusCode();

                                string data = await res.Content.ReadAsStringAsync();
                                Console.WriteLine(data);
                                //req = new HttpRequestMessage(HttpMethod.Get, $"{url}://{binhost}/verifikasi-check");
                                //req.Headers.TryAddWithoutValidation("Authorization", $"Basic {base64token}");

                                //res = await httpClient.SendAsync(req);
                                //res.EnsureSuccessStatusCode();
                                //string data = await res.Content.ReadAsStringAsync();
                                //Console.WriteLine(data);
                                return data.Contains("1");
                            }
                            catch (HttpRequestException ex)
                            {

                                Console.WriteLine(url);
                                Console.WriteLine(ex.Message + " "+ ex.InnerException?.Message);
                                return false;
                            }
                        });
                    }

                    var ret = await Task.WhenAll(tasks);
                    result = ret?.Any(x => x) ?? false;
                }
                while (!result);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                result = false;
            }
            return result;
        }
        async Task Verification()
        {
            if (User is null || OpenBin is null)
                throw new Exception("Invalid Input");
            var binData = await DbService.GetBin(OpenBin.openbinname);
            if (OpenBin.openbinname == Scan && transactionType.HasValue && transactionType.Value != TransactionType.Manual)
            {
                //if (transactionType.HasValue && transactionType.Value == TransactionType.Collection)
                //{
                //    if (binData!.status != "")
                //    {
                //        await dialogService.ShowMessageAsync("Transaction Not Finished", "Bin Not Finished");
                //        return;
                //    }
                //    await Collection();
                //}
                if (transactionType.HasValue && transactionType.Value == TransactionType.Dispose)
                {
                    if (!await SendBinVerif(OpenBin.openbinname))
                    {
                        await dialogService.ShowMessageAsync("Transaction Not Finished", "Bin Not Finished");
                        return;
                    }
                }
                Scan = string.Empty;
                await dialogService.ShowMessageAsync("Verification", OpenBin.activity == 1 ? "Verification Waste process" : "Verification Dispose process");
                ResetStateInput();
            }
            else if (transactionType.HasValue && transactionType.Value != TransactionType.Manual)
                await dialogService.ShowMessageAsync("Verification Failed", "Wrong Container Bin");
//             LoadBins();
        }
        async Task Collection()
        {
            if (User is null || OpenBin is null)
                throw new Exception("Invalid input");
            var activity = new TransactionActivityModel()
            {
                BadgeNo = User!.badgeno,
                Activity = "Collection",
                StationName = ConfigService.Config.hostname,
                FromBinName = OpenBin!.openbinname,
                LoginDate = "",
                ToBinName = "",
                Weight = 0
            };
            var res = await Service.SendTransactionPIDSG(activity);

            ScrapTransactionModel transaction = new ScrapTransactionModel(-1, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), loginDate.ToString("yyyy-MM-dd HH:mm:ss"),
                User.badgeno, Container!.name, OpenBin.openbinname, "ONLINE", ConfigService.Config.hostname, (double)0, "Collection", User.badgeno);
            transaction.Status = res ? "SUCCESS" : "FAILED";
            await DbService.CreateTransaction(transaction);
            await DbService.UpdateStatusBin("", OpenBin.openbinname);
        }
        [RelayCommand]
        public async Task WasteProcess()
        {
            if (string.IsNullOrEmpty(Scan))
                return;
            if (User is null)
                await HandleLogin();
            else if (Container is null)
                await ContainerScan();
            else if (OpenBin is not null)
                await Verification();
            Scan = string.Empty;
        }

        [RelayCommand]
        public async Task TriggerTransaction(BinModel? Bin =null)
        {
            if (Bin is null || Container is null || User is null)
                return;
            var res = await Service.VerifyStep2(User.badgeno, Container.name, Bin.Name);
            ScrapTransactionModel transaction = new ScrapTransactionModel(-1, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), loginDate.ToString("yyyy-MM-dd HH:mm:ss"),
                User.badgeno, Container.name, Bin.Name, "ONLINE", ConfigService.Config.hostname, (double)Container.weightresult, res?.data[0].activity ?? "None", User.badgeno);
            await DbService.CreateTransaction(transaction);
            string message = res?.data[0].status ?? "Error";
            ResetStateInput();
            await dialogService.ShowMessageAsync("Result", message);
//            LoadBins();
        }

        [RelayCommand]
        public  async Task ToggleMode()
        {
            var res=  await WeakReferenceMessenger.Default.Send(new LoginMessage());
            if (res is not null)
                IsAuto = !IsAuto;
        }
        async partial void OnContainerChanged(ContainerBinModel? oldValue, ContainerBinModel? newValue)
        {
            await DbService.SetAppData(nameof(Container), newValue);
        }
        async partial void OnUserChanged(UserModel? oldValue, UserModel? newValue)
        {
            await DbService.SetAppData(nameof(User), newValue);
        }
        async partial void OnOpenBinChanged(BinActivityModel? oldValue, BinActivityModel? newValue)
        {
                await DbService.SetAppData(nameof(OpenBin), newValue);
        }
        async partial void OnMessageChanged(string? oldValue, string newValue)
        {
            await DbService.SetAppData(nameof(Message), newValue);
        }
        partial void OnIsAutoChanged(bool oldValue, bool newValue)
        {
//            await DbService.SetAppData(nameof(IsAuto), newValue);
        }
        public async Task LoadLastTransaction()
        {
            User = await DbService.GetAppData<UserModel>(nameof(User));
            loginDate = await DbService.GetAppData<DateTime>(nameof(loginDate));
            Container = await DbService.GetAppData<ContainerBinModel>(nameof(Container));
            OpenBin = await DbService.GetAppData<BinActivityModel>(nameof(OpenBin));
            Message = await DbService.GetAppData<string>(nameof(Message)) ?? "Scan Badge ID";
            transactionType = await DbService.GetAppData<TransactionType?>(nameof(transactionType)) ?? null;
            //            IsAuto = await DbService.GetAppData<bool>(nameof(IsAuto));
        }
        async Task<bool> CancelTransactionBin(string url,string binname)
        {
            try
            {
                string binhost = await DbService.GetHostname(binname);
                string token = $"root:00000000";
                string base64token = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, $"{url}://{binhost}/resetBin");
                req.Headers.TryAddWithoutValidation("Authorization", $"Basic {base64token}");

                var res = await httpClient.SendAsync(req);
                res.EnsureSuccessStatusCode();
                string data = await res.Content.ReadAsStringAsync();
                return data.Contains("1");
            }
            catch (HttpRequestException ex)
            {

                Console.WriteLine(url);
                Console.WriteLine(ex.Message + " " + ex.InnerException?.Message);
                return false;
            }

        }
        [RelayCommand]
        public async Task Refresh()
        {
            var res = await WeakReferenceMessenger.Default.Send(new LoginMessage());
            if (res is not null)
            {
                Scan = string.Empty;
                if (OpenBin is not null)
                {     
                    await DbService.UpdateStatusBin("", OpenBin.openbinname);
                    string[] urls = ["https", "http"];
                    Task<bool>[] tasks = new Task<bool>[urls.Length];
                    bool result = false;
                    CancellationTokenSource tokenCancel = new CancellationTokenSource();
                    try
                    {
                        tokenCancel.Token.ThrowIfCancellationRequested();
                        tokenCancel.CancelAfter(TimeSpan.FromSeconds(7));
                        do
                        {
                            for (int i = 0; i < tasks.Length; i++)
                            {
                                string url = urls[i];
                                tasks[i] = Task.Run(() => CancelTransactionBin(url, OpenBin.openbinname));
                            }
                            var ret = await Task.WhenAll(tasks);
                            result = ret?.Any(x => x) ?? false;
                        }
                        while (!result);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message + " | "+ ex.StackTrace);
                    }
                }
                ResetStateInput();
            }
        }

    }
}