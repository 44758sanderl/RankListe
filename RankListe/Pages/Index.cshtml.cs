using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

[IgnoreAntiforgeryToken]
public class IndexModel : PageModel
{
    private readonly string _deltagerePath;
    private readonly string _kampePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public IndexModel(IWebHostEnvironment env)
    {
        _deltagerePath = Path.Combine(env.ContentRootPath, "deltagere.json");
        _kampePath = Path.Combine(env.ContentRootPath, "kampe.json");
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetHentDataAsync()
    {
        var deltagere =
            await ReadFromFilAsync<List<Deltager>>(_deltagerePath)
            ?? new List<Deltager>();

        var kampe =
            await ReadFromFilAsync<List<Kamp>>(_kampePath)
            ?? new List<Kamp>();

        var rangliste = deltagere
            .Select(d => new
            {
                d.Navn,
                d.Points,
                WinPct = d.KampeVundet + d.KampeTabt > 0
                    ? Math.Round(
                        (double)d.KampeVundet /
                        (d.KampeVundet + d.KampeTabt) * 100,
                        1)
                    : 0
            })
            .OrderByDescending(d => d.Points)
            .ThenByDescending(d => d.WinPct)
            .ThenBy(d => d.Navn)
            .ToList();

        var senesteKampe = kampe
            .OrderByDescending(k => k.Dato)
            .Take(10)
            .ToList();

        return new JsonResult(new
        {
            rangliste,
            senesteKampe
        });
    }

    public async Task<IActionResult> OnPostTilfoejDeltagerAsync(
        [FromBody] DeltagerIndput? input)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.Navn))
        {
            return BadRequest(new
            {
                success = false,
                message = "Navn mangler."
            });
        }

        var navn = input.Navn.Trim();

        var deltagere =
            await ReadFromFilAsync<List<Deltager>>(_deltagerePath)
            ?? new List<Deltager>();

        var findesAllerede = deltagere.Any(d =>
            d.Navn.Equals(navn, StringComparison.OrdinalIgnoreCase));

        if (findesAllerede)
        {
            return BadRequest(new
            {
                success = false,
                message = "Deltageren findes allerede."
            });
        }

        deltagere.Add(new Deltager
        {
            Navn = navn,
            Points = 0,
            KampeVundet = 0,
            KampeTabt = 0
        });

        await GemTilFilAsync(_deltagerePath, deltagere);

        return new JsonResult(new
        {
            success = true
        });
    }

    public async Task<IActionResult> OnPostRegistrerKampAsync(
    [FromBody] KampInput? input)
    {
        if (input == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "Manglende data."
            });
        }

        if (string.IsNullOrWhiteSpace(input.Vinder) ||
            string.IsNullOrWhiteSpace(input.Taber))
        {
            return BadRequest(new
            {
                success = false,
                message = "Vinder og taber skal vælges."
            });
        }

        if (input.Vinder.Equals(
                input.Taber,
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                success = false,
                message = "Vinder og taber skal være forskellige."
            });
        }

        if (input.VinderScore < 0 || input.TaberScore < 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Points kan ikke være negative."
            });
        }

        var deltagere =
            await ReadFromFilAsync<List<Deltager>>(_deltagerePath)
            ?? new List<Deltager>();

        var kampe =
            await ReadFromFilAsync<List<Kamp>>(_kampePath)
            ?? new List<Kamp>();

        var vinderen = deltagere.FirstOrDefault(d =>
            d.Navn.Equals(
                input.Vinder.Trim(),
                StringComparison.OrdinalIgnoreCase));

        var taberen = deltagere.FirstOrDefault(d =>
            d.Navn.Equals(
                input.Taber.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (vinderen == null || taberen == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "En eller begge spillere findes ikke."
            });
        }

        vinderen.Points += input.VinderScore;
        taberen.Points += input.TaberScore;

        vinderen.KampeVundet++;
        taberen.KampeTabt++;

        var kamp = new Kamp
        {
            Dato = DateTime.Now,
            Tidspunkt = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
            Vinder = vinderen.Navn,
            VinderScore = input.VinderScore,
            TaberScore = input.TaberScore,
            Taber = taberen.Navn
        };

        kampe.Insert(0, kamp);

        await GemTilFilAsync(_deltagerePath, deltagere);
        await GemTilFilAsync(_kampePath, kampe);

        return new JsonResult(new
        {
            success = true
        });
    }


    public async Task<IActionResult> OnGetDownloadDeltagereAsync()
    {
        if (!System.IO.File.Exists(_deltagerePath))
        {
            return NotFound();
        }

        var bytes =
            await System.IO.File.ReadAllBytesAsync(_deltagerePath);

        return File(
            bytes,
            "application/json",
            "deltagere.json");
    }

    private async Task<T?> ReadFromFilAsync<T>(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return default;
        }

        var json =
            await System.IO.File.ReadAllTextAsync(filePath);

        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(
            json,
            JsonOptions);
    }

    private async Task GemTilFilAsync<T>(
        string filePath,
        T data)
    {
        var json =
            JsonSerializer.Serialize(
                data,
                JsonOptions);

        await System.IO.File.WriteAllTextAsync(
            filePath,
            json);
    }
}

public class Deltager
{
    public string Navn { get; set; } = string.Empty;
    public int Points { get; set; }
    public int KampeVundet { get; set; }
    public int KampeTabt { get; set; }
}

public class Kamp
{
    public DateTime Dato { get; set; }
    public string Tidspunkt { get; set; } = string.Empty;
    public string Vinder { get; set; } = string.Empty;
    public int VinderScore { get; set; }
    public int TaberScore { get; set; }
    public string Taber { get; set; } = string.Empty;
}

public class DeltagerIndput
{
    public string Navn { get; set; } = string.Empty;
}

public class KampInput
{
    public string Vinder { get; set; } = string.Empty;
    public string Taber { get; set; } = string.Empty;
    public int VinderScore { get; set; }
    public int TaberScore { get; set; }
}
