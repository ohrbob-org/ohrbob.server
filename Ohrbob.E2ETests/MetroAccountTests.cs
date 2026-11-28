using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 9 of the Metro restyle: the account overview is a 5/3/2/1 set of square
/// hairline tiles, the panes are hairline panels, the theme selector is three
/// square radio tiles, the morebob upsell is a flat yellow panel, and the QR
/// modal and delete page use the Metro surfaces.
/// Every assertion reads computed styles, not class names.
/// </summary>
public class MetroAccountTests : E2EPageTest
{
    private string _accountId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
    };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        SignIn();

        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");

        // A brand-new onboarding account stays on the free tier, so the morebob
        // upsell renders on the account pane.
        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Metro Account", $"metro-account-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);
        _accountId = account.Id;

        // Two keys make the API-key rows (mono token, QR button, revoke) render;
        // the first row is not the table's last row, so its hairline rule shows.
        var apiKeys = _factory.Services.GetRequiredService<ApiKeysApiService>();
        foreach (var name in new[] { "Living Room", "Kitchen Speaker" })
        {
            await apiKeys.CreateApiKeyAsync(_accountId, new ApiKey
            {
                AccountId = _accountId,
                Name = name,
                ProfileId = string.Empty
            });
        }

        SetAccount(_accountId);
        _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        SetAccount(string.Empty);
        SignIn();
    }

    [Test]
    public async Task AccountTabsAndUpgradeCardAreMetro()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Expect(Page.Locator(".account-tile")).ToHaveCountAsync(5);
        var m = await Page.EvaluateAsync<double[]>(@"() => {
            const tile = document.querySelector('.account-tile');
            const panel = document.querySelector('.morebob-panel');
            return [parseFloat(getComputedStyle(tile).borderRadius),
                    panel ? 1 : 0,
                    panel ? getComputedStyle(panel).backgroundColor.match(/[\d.]+/g).slice(0,3).map(Number)[0] : 0,
                    panel ? getComputedStyle(panel).backgroundColor.match(/[\d.]+/g).slice(0,3).map(Number)[1] : 0];
        }");
        Assert.That(m[0], Is.EqualTo(0));
        Assert.That(m[1], Is.EqualTo(1));
        Assert.That(m[2], Is.EqualTo(253));
        Assert.That(m[3], Is.EqualTo(178));

        // The upsell belongs to the (initially hidden) account pane.
        await Page.ClickAsync("#account-tab");
        await Expect(Page.Locator(".morebob-panel")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AccountStatusLabelMeetsAAContrast()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");

        // Tier upgrades are disabled in the test API, so probe the real stylesheet
        // with the same element the Morebob panel renders.
        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const el = document.createElement('p');
            el.className = 'account-panel__status';
            el.textContent = 'You are on Morebob.';
            document.querySelector('#main-content').appendChild(el);
            const style = getComputedStyle(el);
            const bg = getComputedStyle(document.body).backgroundColor;
            const channels = (value) => value.match(/[\d.]+/g).slice(0, 3).map(Number);
            const luminance = ([r, g, b]) => {
                const lin = (c) => { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
                return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
            };
            const a = luminance(channels(style.color));
            const b = luminance(channels(bg));
            const contrast = (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
            const result = [style.color, style.fontSize, contrast.toFixed(2)];
            el.remove();
            return result;
        }");
        Assert.That(m[0], Is.EqualTo("rgb(194, 65, 12)"), "the small status label uses the accessible now-accent orange");
        Assert.That(m[1], Is.EqualTo("11px"), "the status label is small text");
        Assert.That(double.Parse(m[2], System.Globalization.CultureInfo.InvariantCulture),
            Is.GreaterThanOrEqualTo(4.5), "the 11 px status label meets AA contrast");
    }

    [Test]
    public async Task AccountTiles_UseFiveThreeTwoOneGridAndOrangeActiveRule()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Expect(Page.Locator(".account-tile").First).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const nav = document.querySelector('#accountTabsNav');
            const tile = document.querySelector('.account-tile');
            const title = tile.querySelector('.account-tile__title');
            const kicker = tile.querySelector('.account-tile__kicker');
            const active = document.querySelector('.account-tile.active');
            const activeStyle = getComputedStyle(active);
            return [getComputedStyle(nav).display,
                    getComputedStyle(nav).gridTemplateColumns,
                    getComputedStyle(tile).borderRadius,
                    getComputedStyle(tile).borderTopWidth,
                    getComputedStyle(title).fontFamily,
                    getComputedStyle(title).fontSize,
                    getComputedStyle(kicker).fontSize,
                    getComputedStyle(kicker).textTransform,
                    activeStyle.boxShadow,
                    activeStyle.borderBottomColor];
        }");
        Assert.That(m[0], Is.EqualTo("grid"), "the overview is a grid");
        Assert.That(m[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(5),
            "five tiles sit in one row on desktop");
        Assert.That(m[2], Is.EqualTo("0px"), "tiles are square");
        Assert.That(m[3], Is.EqualTo("1px"), "tiles sit on a 1 px hairline");
        Assert.That(m[4], Does.Contain("Sulphur"), "tile titles use the display face");
        Assert.That(m[5], Is.EqualTo("30px"), "tile titles are 30 px");
        Assert.That(m[6], Is.EqualTo("11px"), "tile kickers are 11 px");
        Assert.That(m[7], Is.EqualTo("lowercase"), "tile kickers are lowercase");
        Assert.That(m[8], Does.Contain("inset"), "the active tile gets an inset rule");
        Assert.That(m[8], Does.Contain("rgb(252, 93, 0)"), "the active rule is orange");

        // 5 / 3 / 2 / 1 columns at the breakpoints.
        await Page.SetViewportSizeAsync(1000, 900);
        Assert.That(await ColumnCountAsync(), Is.EqualTo(3), "three columns below 1200 px");
        await Page.SetViewportSizeAsync(800, 900);
        Assert.That(await ColumnCountAsync(), Is.EqualTo(2), "two columns below 900 px");
        await Page.SetViewportSizeAsync(390, 844);
        Assert.That(await ColumnCountAsync(), Is.EqualTo(1), "one column below 576 px");
    }

    [Test]
    public async Task AccountTile_OpensDetailsAndBackReturnsToOverview()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");

        await Expect(Page.Locator("#accountTabsNav")).ToBeVisibleAsync();
        await Expect(Page.Locator("#accountTabsContent")).ToBeHiddenAsync();

        await Page.ClickAsync("#appearance-tab");

        await Expect(Page.Locator("#accountTabsNav")).ToBeHiddenAsync();
        await Expect(Page.Locator("#accountTabsContent")).ToBeVisibleAsync();
        await Expect(Page.Locator("#appearance-pane")).ToBeVisibleAsync();
        await Expect(Page.Locator("#accountTabsBack")).ToBeVisibleAsync();

        await Page.ClickAsync("#accountTabsBack");

        await Expect(Page.Locator("#accountTabsNav")).ToBeVisibleAsync();
        await Expect(Page.Locator("#accountTabsContent")).ToBeHiddenAsync();
    }

    [Test]
    public async Task AccountPanel_UsesHairlineTableAndLowercaseLabels()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Page.ClickAsync("#account-tab");
        await Expect(Page.Locator("#account-pane .account-panel").First).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const panel = document.querySelector('#account-pane .account-panel');
            const title = panel.querySelector('.account-panel__title');
            const th = panel.querySelector('.account-table th');
            const td = panel.querySelector('.account-table td');
            const thStyle = getComputedStyle(th);
            const tdStyle = getComputedStyle(td);
            return [getComputedStyle(panel).borderTopWidth,
                    getComputedStyle(panel).borderTopColor,
                    getComputedStyle(title).fontFamily,
                    thStyle.borderBottomWidth,
                    thStyle.textTransform,
                    thStyle.fontSize,
                    thStyle.fontWeight,
                    tdStyle.borderBottomWidth,
                    tdStyle.verticalAlign,
                    getComputedStyle(panel).borderRadius];
        }");
        Assert.That(m[0], Is.EqualTo("1px"), "panels are hairline-separated");
        Assert.That(m[1], Is.EqualTo("rgba(0, 0, 0, 0)"), "the hairline is gone");
        Assert.That(m[2], Does.Contain("Sulphur"), "panel titles use the display face");
        Assert.That(m[3], Is.EqualTo("1px"), "table rows have a hairline rule");
        Assert.That(m[4], Is.EqualTo("lowercase"), "row labels are lowercase");
        Assert.That(m[5], Is.EqualTo("11px"), "row labels are 11 px");
        Assert.That(m[6], Is.EqualTo("700"), "row labels are bold");
        Assert.That(m[7], Is.EqualTo("1px"), "table cells keep the row rule");
        Assert.That(m[8], Is.EqualTo("top"), "table cells align to the top");
        Assert.That(m[9], Is.EqualTo("0px"), "panels are square");
    }

    [Test]
    public async Task MorebobPanel_IsFlatYellowWithOrangeBarAndCta()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Page.ClickAsync("#account-tab");
        await Expect(Page.Locator(".morebob-panel")).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const panel = document.querySelector('.morebob-panel');
            const bar = panel.querySelector('.morebob-panel__bar');
            const cta = panel.querySelector('.morebob-panel__cta');
            const panelStyle = getComputedStyle(panel);
            const barStyle = getComputedStyle(bar);
            const ctaStyle = getComputedStyle(cta);
            return [panelStyle.backgroundColor,
                    panelStyle.color,
                    panelStyle.backgroundImage,
                    panelStyle.boxShadow,
                    panelStyle.borderRadius,
                    barStyle.width,
                    barStyle.height,
                    barStyle.backgroundColor,
                    ctaStyle.backgroundColor,
                    ctaStyle.borderRadius,
                    panel.querySelectorAll('img').length.toString()];
        }");
        Assert.That(m[0], Is.EqualTo("rgb(253, 178, 0)"), "the upgrade panel is flat yellow");
        Assert.That(m[1], Is.EqualTo("rgb(0, 25, 87)"), "the panel text is navy");
        Assert.That(m[2], Is.EqualTo("none"), "the panel has no gradient");
        Assert.That(m[3], Is.EqualTo("none"), "the panel has no glow or shadow");
        Assert.That(m[4], Is.EqualTo("0px"), "the panel is square");
        Assert.That(m[5], Is.EqualTo("46px"), "the yellow panel carries a 46 px bar");
        Assert.That(m[6], Is.EqualTo("5px"), "the bar is 5 px tall");
        Assert.That(m[7], Is.EqualTo("rgb(252, 93, 0)"), "the bar is orange");
        Assert.That(m[8], Is.EqualTo("rgb(252, 93, 0)"), "the call to action is orange");
        Assert.That(m[9], Is.EqualTo("0px"), "the call to action is square");
        Assert.That(m[10], Is.EqualTo("0"), "the watermark image is gone");

        await Expect(Page.Locator(".morebob-panel form[action='/Account/Upgrade'] button"))
            .ToContainTextAsync("Upgrade to Morebob");
    }

    [Test]
    public async Task ThemeChoices_AreSquareTilesWithOrangeCheckedRing()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Page.ClickAsync("#appearance-tab");
        await Expect(Page.Locator("#appearance-pane")).ToBeVisibleAsync();

        await Expect(Page.Locator(".theme-choice")).ToHaveCountAsync(3);
        await Expect(Page.Locator("#themeSystem")).ToBeCheckedAsync();

        var before = await Page.EvaluateAsync<string[]>(@"() => {
            const label = document.querySelector('.theme-choice__label');
            const swatch = document.querySelector('.theme-choice__swatch--light');
            const dark = document.querySelector('.theme-choice__swatch--dark');
            const name = document.querySelector('.theme-choice__name');
            const labelStyle = getComputedStyle(label);
            return [labelStyle.borderRadius,
                    labelStyle.borderTopWidth,
                    getComputedStyle(swatch).backgroundColor,
                    getComputedStyle(dark).backgroundColor,
                    getComputedStyle(name).fontFamily,
                    getComputedStyle(name).fontSize];
        }");
        Assert.That(before[0], Is.EqualTo("0px"), "theme choices are square");
        Assert.That(before[1], Is.EqualTo("1px"), "theme choices sit on a hairline");
        Assert.That(before[2], Is.EqualTo("rgb(255, 255, 255)"), "the light preview is white");
        Assert.That(before[3], Is.EqualTo("rgb(0, 25, 87)"), "the dark preview is navy");
        Assert.That(before[4], Does.Contain("Sulphur"), "theme names use the display face");
        Assert.That(before[5], Is.EqualTo("24px"), "theme names are 24 px");

        await Page.Locator("label[for='themeDark']").ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark",
            new() { Timeout = 5000 });
        await Expect(Page.Locator("#themeDark")).ToBeCheckedAsync();

        var checkedStyle = await Page.EvaluateAsync<string>(@"() => {
            const label = document.querySelector('#themeDark').nextElementSibling;
            return getComputedStyle(label).boxShadow;
        }");
        Assert.That(checkedStyle, Does.Contain("inset"), "the checked tile gets an inset ring");
        Assert.That(checkedStyle, Does.Contain("rgb(252, 93, 0)"), "the checked ring is orange");
    }

    [Test]
    public async Task ApiKeys_AreMonoOnHairlineRows_AndQrModalIsMetro()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Page.ClickAsync("#api-keys-tab");
        await Expect(Page.Locator("#api-keys-pane")).ToBeVisibleAsync();
        await Expect(Page.Locator(".api-key-row")).ToHaveCountAsync(2);

        var row = await Page.EvaluateAsync<string[]>(@"() => {
            const row = document.querySelector('.api-key-row');
            const token = row.querySelector('code');
            const action = row.querySelector('.api-key-action');
            const cellStyle = getComputedStyle(row.querySelector('td'));
            const actionStyle = getComputedStyle(action);
            return [getComputedStyle(token).fontFamily,
                    getComputedStyle(token).fontSize,
                    cellStyle.borderBottomWidth,
                    actionStyle.borderRadius,
                    actionStyle.borderTopWidth,
                    actionStyle.minHeight];
        }");
        Assert.That(row[0].ToLowerInvariant(), Does.Contain("mono"), "API keys are mono");
        Assert.That(row[1], Is.EqualTo("11px"), "API keys are 11 px");
        Assert.That(row[2], Is.EqualTo("1px"), "key rows have a hairline rule");
        Assert.That(row[3], Is.EqualTo("0px"), "key actions are square");
        Assert.That(row[4], Is.EqualTo("1px"), "key actions sit on a hairline");
        Assert.That(row[5], Is.EqualTo("44px"), "key actions meet the 44 px touch target");

        Assert.That(await Page.EvaluateAsync<bool>("() => typeof window.showConnectionQr === 'function'"),
            Is.True, "showConnectionQr stays available");

        await Page.Locator("#api-keys-pane button:has-text('Show QR')").First.ClickAsync();
        await Expect(Page.Locator("#qrCodeModal")).ToBeVisibleAsync(new() { Timeout = 15000 });
        // qrcodejs renders a canvas or an image depending on data-URI support.
        await Expect(Page.Locator("#qrcode-container img:visible, #qrcode-container canvas:visible"))
            .ToHaveCountAsync(1, new() { Timeout = 15000 });

        var modal = await Page.EvaluateAsync<string[]>(@"() => {
            const content = document.querySelector('#qrCodeModal .modal-content');
            const accent = document.querySelector('#qrCodeModal .metro-modal-accent');
            const frame = document.querySelector('#qrcode-container');
            const value = document.querySelector('#qrDeepLink');
            const label = document.querySelector('#qrCodeModal .form-label');
            return [getComputedStyle(content).borderRadius,
                    getComputedStyle(accent).height,
                    getComputedStyle(accent).backgroundColor,
                    getComputedStyle(frame).backgroundColor,
                    getComputedStyle(frame).borderTopWidth,
                    getComputedStyle(frame).borderRadius,
                    getComputedStyle(value).fontFamily,
                    getComputedStyle(label).textTransform,
                    getComputedStyle(label).fontSize,
                    value.textContent.trim()];
        }");
        Assert.That(modal[0], Is.EqualTo("0px"), "the QR modal is square");
        Assert.That(modal[1], Is.EqualTo("6px"), "the QR modal has the Metro accent bar");
        Assert.That(modal[2], Is.EqualTo("rgb(252, 93, 0)"), "the accent bar is orange");
        Assert.That(modal[3], Is.EqualTo("rgb(255, 255, 255)"), "the QR area stays white");
        Assert.That(modal[4], Is.EqualTo("1px"), "the QR area has a hairline");
        Assert.That(modal[5], Is.EqualTo("0px"), "the QR area is square");
        Assert.That(modal[6].ToLowerInvariant(), Does.Contain("mono"), "deep link values are mono");
        Assert.That(modal[7], Is.EqualTo("lowercase"), "QR labels are lowercase");
        Assert.That(modal[8], Is.EqualTo("11px"), "QR labels are 11 px");
        Assert.That(modal[9], Does.StartWith("ohrbob://connect?"), "the deep link is filled in");

        await Expect(Page.Locator("#qrServerUrl")).ToHaveValueAsync(new Regex(".+"));
        await Expect(Page.Locator("#qrToken")).ToHaveValueAsync(new Regex(".+"));
    }

    [Test]
    public async Task CreatingApiKey_AfterHtmxSwap_ShowsToastAndOpensFreshKeyQr()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Page.ClickAsync("#api-keys-tab");
        await Expect(Page.Locator("#api-keys-pane")).ToBeVisibleAsync();

        // A document marker would disappear on a full page reload. The boosted
        // create form must swap #main-content instead, keeping playback alive.
        await Page.EvaluateAsync("() => { window.__ohrbobSameDocument = true; }");

        await Page.FillAsync("#keyName", "Hallway Speaker");
        await Page.Locator("#api-keys-pane form[action='/Account/AddApiKey'] button[type='submit']").ClickAsync();

        // TempData from the create request must be surfaced after the swap.
        await Expect(Page.Locator("#liveToast")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.Locator("#liveToast")).ToHaveClassAsync(new Regex(@"\bis-success\b"));
        await Expect(Page.Locator("#toast-body-text"))
            .ToContainTextAsync("API key created successfully.");

        // ... and the freshly created key opens its QR modal, fully populated.
        await Expect(Page.Locator("#qrCodeModal")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.Locator("#qrKeyName")).ToHaveTextAsync("Hallway Speaker");
        await Expect(Page.Locator("#qrToken")).ToHaveValueAsync(new Regex(".+"));
        await Expect(Page.Locator("#qrServerUrl")).ToHaveValueAsync(new Regex(".+"));
        Assert.That(await Page.Locator("#qrDeepLink").TextContentAsync(),
            Does.StartWith("ohrbob://connect?"));
        await Expect(Page.Locator("#qrcode-container img:visible, #qrcode-container canvas:visible"))
            .ToHaveCountAsync(1, new() { Timeout = 15000 });

        Assert.That(await Page.EvaluateAsync<bool>("() => window.__ohrbobSameDocument === true"),
            Is.True, "the create form must submit through htmx, keeping the document and playback alive");

        // The swapped-in account page contains the new key.
        await Expect(Page.Locator(".api-key-row", new() { HasTextString = "Hallway Speaker" }))
            .ToHaveCountAsync(1);
    }

    [Test]
    public async Task DeletePage_UsesDangerPanelWithRedRule()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Page.ClickAsync("#delete-tab");
        var link = Page.Locator("#delete-pane a[href='/Account/Delete']");
        await Expect(link).ToBeVisibleAsync();
        await link.ClickAsync();

        await Page.WaitForURLAsync(new Regex("/Account/Delete$"), new() { Timeout = 15000 });
        await Expect(Page.Locator(".danger-panel")).ToBeVisibleAsync();
        await Expect(Page.Locator(".danger-panel")).ToContainTextAsync("This cannot be undone.");

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const panel = document.querySelector('.danger-panel');
            const submit = document.querySelector(""form[action='/Account/Delete'] button[type='submit']"");
            const cancel = document.querySelector('.danger-actions a');
            return [getComputedStyle(panel).borderLeftWidth,
                    getComputedStyle(panel).borderLeftColor,
                    getComputedStyle(panel).borderRadius,
                    getComputedStyle(submit).backgroundColor,
                    getComputedStyle(submit).borderRadius,
                    getComputedStyle(cancel).backgroundColor];
        }");
        Assert.That(m[0], Is.EqualTo("4px"), "the danger panel has a 4 px left rule");
        Assert.That(m[1], Is.EqualTo("rgb(220, 53, 69)"), "the left rule is red");
        Assert.That(m[2], Is.EqualTo("0px"), "the danger panel is square");
        Assert.That(m[3], Is.EqualTo("rgb(220, 53, 69)"), "delete is the red primary action");
        Assert.That(m[4], Is.EqualTo("0px"), "the delete button is square");
        Assert.That(m[5], Is.EqualTo("rgba(0, 0, 0, 0)"), "cancel is a ghost action");
    }

    [Test]
    public async Task Account_Fit390WithoutPageOverflow()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Expect(Page.Locator(".account-tile").First).ToBeVisibleAsync();

        var overview = await Page.EvaluateAsync<string[]>(@"() => {
            const nav = document.querySelector('#accountTabsNav');
            const tile = document.querySelector('.account-tile');
            return [document.documentElement.scrollWidth.toString(),
                    window.innerWidth.toString(),
                    getComputedStyle(nav).gridTemplateColumns,
                    tile.getBoundingClientRect().right.toString()];
        }");
        Assert.That(int.Parse(overview[0]), Is.LessThanOrEqualTo(int.Parse(overview[1]) + 1),
            "the account page does not scroll sideways at 390 px");
        Assert.That(overview[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(1),
            "the tiles stack in one column at 390 px");
        Assert.That(double.Parse(overview[3]), Is.LessThanOrEqualTo(double.Parse(overview[1])),
            "the account tile stays inside the viewport");

        await Page.ClickAsync("#account-tab");
        await Expect(Page.Locator(".morebob-panel")).ToBeVisibleAsync();
        var details = await Page.EvaluateAsync<string[]>(@"() => {
            const table = document.querySelector('#account-pane .account-table');
            const panel = document.querySelector('.morebob-panel');
            return [document.documentElement.scrollWidth.toString(),
                    window.innerWidth.toString(),
                    table.getBoundingClientRect().right.toString(),
                    getComputedStyle(panel.querySelector('.morebob-panel__features')).gridTemplateColumns];
        }");
        Assert.That(int.Parse(details[0]), Is.LessThanOrEqualTo(int.Parse(details[1]) + 1),
            "the account details do not scroll sideways at 390 px");
        Assert.That(double.Parse(details[2]), Is.LessThanOrEqualTo(double.Parse(details[1])),
            "the details table stays inside the viewport");
        Assert.That(details[3].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(1),
            "the morebob feature list stacks at 390 px");

        await Page.ClickAsync("#accountTabsBack");
        await Page.ClickAsync("#api-keys-tab");
        await Expect(Page.Locator(".api-key-row").First).ToBeVisibleAsync();
        var apiKeys = await Page.EvaluateAsync<string[]>(@"() => {
            const wrap = document.querySelector('.api-key-table-wrap');
            return [document.documentElement.scrollWidth.toString(),
                    window.innerWidth.toString(),
                    wrap.getBoundingClientRect().right.toString()];
        }");
        Assert.That(int.Parse(apiKeys[0]), Is.LessThanOrEqualTo(int.Parse(apiKeys[1]) + 1),
            "the API-key pane does not scroll sideways at 390 px");
        Assert.That(double.Parse(apiKeys[2]), Is.LessThanOrEqualTo(double.Parse(apiKeys[1])),
            "the API-key table stays inside the viewport");
    }

    private async Task<int> ColumnCountAsync()
    {
        var columns = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('#accountTabsNav')).gridTemplateColumns");
        return columns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
