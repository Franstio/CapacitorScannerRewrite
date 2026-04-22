using CapacitorScanner.Core.Model;
using CapacitorScanner.Core.Model.FromBin;
using CapacitorScanner.Core.Model.LocalDb;
using CapacitorScanner.Core.Model.PIDSG;
using CapacitorScanner.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace CapacitorScanner.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BinController : ControllerBase
    {
        private readonly BinLocalDbService _binLocalDbService;
        private readonly PIDSGService pidsgService;
        private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private readonly ConfigService configService;
        public BinController(BinLocalDbService binLocalDbService, PIDSGService pidsgService, ConfigService configService)
        {
            _binLocalDbService = binLocalDbService;
            this.pidsgService = pidsgService;
            this.configService = configService;
        }
        [HttpGet("{name}")]
        public async Task<BinLocalModel?> GetBinData(string name)
        {
            return await _binLocalDbService.GetBin(name);
        }
        [HttpGet("")]
        public async Task<IEnumerable<BinLocalModel>> GetBinData()
        {
            return await _binLocalDbService.GetBin();
        }
        [HttpPut("")]
        public async    Task<IActionResult> UpdateWeightHostname([FromBody] UpdateBinRequestModel bin)
        {
            var dataBin = await _binLocalDbService.GetBin(bin.BinName);

            if (dataBin == null)
                return NotFound();
            await this.pidsgService.SendWeight(bin.BinName, bin.Weight);
            dataBin.weight = bin.Weight;
            dataBin.hostname = bin.Hostname;
            await _binLocalDbService.UpdateBin(dataBin);
            return Ok();
        }
        [HttpGet("check")]
        public async Task<IActionResult> CheckTransaction([FromQuery]string name)
        {
            var dataBin = await _binLocalDbService.GetBin(name);
            if (dataBin is null)
                return NotFound();
            var response = new
            {
                doorstatus = (dataBin.status == "" ? 0 : (dataBin.status == "Dispose" ? 1 : 2)),
                capacity = dataBin.maxweight,
                isactivated = "1",
                name  = dataBin.bin,
                weight = dataBin.binweight,
                lastfrombinname =   dataBin.lastfrombinname,
                weightresult = dataBin.weight,
                lastbadgeno = dataBin.lastbadgeno,
                weightsystem = dataBin.weightsystem
            };
            return Ok(new {
                data=new object[] { response },
                success=true
            });
        }

        [HttpPost("")]
        public async Task<IActionResult> AddBin([FromBody] BinLocalModel bin)
        {
            var dataBin = await _binLocalDbService.GetBin(bin.bin);
            if (dataBin != null)
                return BadRequest("Bin already exists");
            await _binLocalDbService.InsertBinHost(bin);
            return Ok();
        }
        [HttpPost("Transaction")]
        public async Task<IActionResult> SaveTransaction(TransactionActivityModel transaction)
        {

            var ok = Ok(new
            {
                success = true,
                result = "Success"
            });
            try
            {
                await semaphore.WaitAsync();
                transaction.LoginDate = DateTime.Now.ToString("yyyy-MM-dd");
                transaction.StationName = configService.Config.hostname;
                ScrapTransactionModel scraprecord = new ScrapTransactionModel(-1, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), transaction.LoginDate!,
                transaction.BadgeNo!, transaction.FromBinName!, transaction.ToBinName!, "ONLINE", configService.Config.hostname, Convert.ToDouble(transaction.Weight?.ToString("0.00") ?? "0"), transaction.Activity!, transaction.BadgeNo!);
                scraprecord.Code = DateTime.Now.ToString("yyyyMMdd_hhmmss");
                scraprecord.RealWeight = Convert.ToDouble( transaction.realweight.ToString()!);
                scraprecord.PrevWeight = Convert.ToDouble(transaction.prevweight.ToString()!);
                scraprecord.Status = "READY";
                await _binLocalDbService.UpdateStatusBin("", string.IsNullOrEmpty(transaction.ToBinName) ? transaction.FromBinName! : transaction.ToBinName!);
                await _binLocalDbService.CreateTransaction(scraprecord);
                return ok;
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
                return StatusCode(500, e.Message + "\n"+e.InnerException?.Message +"\n"+e.StackTrace );
            }
            finally
            {
                semaphore.Release();
            }
        }
        [HttpGet("status/{name}/{status}")]
        public async Task<IActionResult> SetStatusBin(string name,int status)
        {
            if ((await _binLocalDbService.GetBin(name)) is null)
                return NotFound();
            string statusBin = "";
            switch(status)
            {
                case 1:
                    statusBin = "dispose";
                    break;
                case 2:
                    statusBin = "collection";
                    break;
                default:
                    statusBin = "";
                    break;
            }
            await _binLocalDbService.UpdateStatusBin(statusBin, name);
            return Ok();
        }
    }
}
