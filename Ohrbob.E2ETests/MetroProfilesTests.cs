using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 8 of the Metro restyle: profiles are square hairline tiles, the colour
/// picker is a row of 32 px square swatches with a 96 px preview, and the edit
/// page puts details and allowed stations in two columns.
/// Every assertion reads computed styles, not class names.
/// </summary>
[NonParallelizable]
public class MetroProfilesTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _accountId = string.Empty;
    private string _profileId = string.Empty;
    private string _stationId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
    };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        _factory = new PlaywrightWebApplicationFactory();

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Metro Profiles", $"metro-profiles-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);
        _accountId = account.Id;

        var profileService = _factory.Services.GetRequiredService<ProfilesApiService>();
        var profiles = await profileService.GetProfilesByAccountIdAsync(_accountId);
        if (profiles == null || profiles.Count == 0)
        {
            await profileService.CreateProfileAsync(_accountId, new Profile { Name = "Main Profile" });
            profiles = await profileService.GetProfilesByAccountIdAsync(_accountId);
        }
        _profileId = profiles!.First().Id;

        // The edit page lists every account station. A feedless station still renders
        // the mosaic fallback, so the tile test does not depend on podcast-testcasts.
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Metro Profile Station",
            Feeds = Array.Empty<string>()
        });
        _stationId = station!.Id;
        await profileService.AddStationToProfileAsync(_profileId, _stationId);

        TestAuthHandler.TestAccountId = _accountId;
        _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task ProfileCardsAndPickerAreMetro()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles");
        var card = Page.Locator(".profile-card").First;
        await Expect(card).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<double[]>(@"() => {
            const card = document.querySelector('.profile-card');
            const avatar = card.querySelector('.profile-avatar');
            return [parseFloat(getComputedStyle(card).borderRadius),
                    parseFloat(getComputedStyle(avatar).width),
                    parseFloat(getComputedStyle(avatar).borderTopLeftRadius)];
        }");
        Assert.That(m, Is.EqualTo(new[] { 0d, 64d, 0d }));

        // The header avatar keeps its own 34 px square, independent of the card tile.
        var header = await Page.EvaluateAsync<double[]>(@"() => {
            const avatar = document.querySelector('#profileDropdown .profile-avatar');
            return [parseFloat(getComputedStyle(avatar).width),
                    parseFloat(getComputedStyle(avatar).height),
                    parseFloat(getComputedStyle(avatar).borderTopLeftRadius)];
        }");
        Assert.That(header, Is.EqualTo(new[] { 34d, 34d, 0d }));

        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles/Create");
        var swatch = await Page.EvaluateAsync<double[]>(@"() => {
            const s = document.querySelector('.profile-color-swatch');
            const p = document.querySelector('.profile-color-preview');
            return [parseFloat(getComputedStyle(s).width), parseFloat(getComputedStyle(s).borderRadius),
                    parseFloat(getComputedStyle(p).width)];
        }");
        Assert.That(swatch[0], Is.EqualTo(32));
        Assert.That(swatch[1], Is.EqualTo(0));
        Assert.That(swatch[2], Is.EqualTo(96));
    }

    [Test]
    public async Task ProfileCard_ShowsActiveStateMonoTokenAndActions()
    {
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "Ohrbob_ActiveProfileId", Value = _profileId, Url = _factory.ServerAddress }
        });
        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles");

        var card = Page.Locator(".profile-card.is-active").First;
        await Expect(card).ToBeVisibleAsync();
        await Expect(card.Locator(".profile-active")).ToHaveTextAsync("active");

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const card = document.querySelector('.profile-card.is-active');
            const name = card.querySelector('.profile-card__name');
            const token = card.querySelector('.profile-card__token code');
            const label = card.querySelector('.profile-active');
            const actions = card.querySelector('.profile-card__actions');
            const nameStyle = getComputedStyle(name);
            const labelStyle = getComputedStyle(label);
            const channels = (value) => value.match(/[\d.]+/g).slice(0, 3).map(Number);
            const luminance = ([r, g, b]) => {
                const lin = (c) => { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
                return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
            };
            const contrast = (fg, bg) => {
                const a = luminance(channels(fg));
                const b = luminance(channels(bg));
                return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
            };
            return [nameStyle.fontFamily, nameStyle.fontSize,
                    labelStyle.color, labelStyle.textTransform,
                    getComputedStyle(token).fontFamily, getComputedStyle(token).fontSize,
                    getComputedStyle(card).backgroundColor,
                    getComputedStyle(actions).borderTopWidth,
                    getComputedStyle(card).boxShadow,
                    contrast(labelStyle.color, getComputedStyle(card).backgroundColor).toFixed(2)];
        }");
        Assert.That(m[0], Does.Contain("Sulphur"), "the profile name uses the display face");
        Assert.That(m[1], Is.EqualTo("24px"), "the profile name is 24 px");
        Assert.That(m[2], Is.EqualTo("rgb(194, 65, 12)"), "the active label uses the accessible now-accent orange");
        Assert.That(m[3], Is.EqualTo("uppercase"), "the active label is uppercase");
        Assert.That(m[4].ToLowerInvariant(), Does.Contain("mono"), "the stream token is mono");
        Assert.That(m[5], Is.EqualTo("11px"), "the stream token is 11 px");
        Assert.That(m[6], Is.EqualTo("rgb(255, 255, 255)"), "the card sits on a tile surface");
        Assert.That(m[7], Is.EqualTo("1px"), "the action footer is hairline-separated");
        Assert.That(m[8], Does.Contain("inset"), "the active tile gets an inset ring");
        Assert.That(m[8], Does.Contain("rgb(252, 93, 0)"), "the active ring is orange");
        Assert.That(double.Parse(m[9], System.Globalization.CultureInfo.InvariantCulture),
            Is.GreaterThanOrEqualTo(4.5), "the 10 px active label meets AA contrast");
    }

    [Test]
    public async Task ColorPicker_SelectsSwatchAndUpdatesPreview()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles/Create");

        await Page.Locator(".profile-color-swatch[data-color='#3b82f6']").ClickAsync();

        await Expect(Page.Locator("[data-color-value]")).ToHaveValueAsync("#3b82f6");
        await Expect(Page.Locator("[data-color-preview]"))
            .ToHaveCSSAsync("background-color", "rgb(59, 130, 246)");

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const picked = Array.from(document.querySelectorAll('.profile-color-swatch'))
                .find(s => s.dataset.color === '#3b82f6');
            const custom = document.querySelector('.profile-color-custom');
            const preview = document.querySelector('[data-color-preview]');
            const pickedStyle = getComputedStyle(picked);
            const customStyle = getComputedStyle(custom);
            const previewStyle = getComputedStyle(preview);
            return [picked.classList.contains('is-selected') ? 'yes' : 'no',
                    pickedStyle.outlineWidth, pickedStyle.outlineStyle, pickedStyle.outlineColor,
                    customStyle.backgroundImage, customStyle.width, customStyle.borderRadius,
                    previewStyle.width, previewStyle.height,
                    previewStyle.fontFamily, previewStyle.fontSize];
        }");
        Assert.That(m[0], Is.EqualTo("yes"), "the clicked swatch is selected");
        Assert.That(m[1], Is.EqualTo("2px"), "the selected swatch is outlined by 2 px");
        Assert.That(m[3], Is.EqualTo("rgb(252, 93, 0)"), "the selection outline is orange");
        Assert.That(m[4], Does.Contain("conic-gradient"), "the custom swatch is a conic gradient");
        Assert.That(m[5], Is.EqualTo("32px"), "the custom swatch matches the 32 px swatches");
        Assert.That(m[6], Is.EqualTo("0px"), "the custom swatch is square");
        Assert.That(m[7], Is.EqualTo("96px"), "the preview is 96 px wide");
        Assert.That(m[8], Is.EqualTo("96px"), "the preview is 96 px tall");
        Assert.That(m[9], Does.Contain("Sulphur"), "the preview initial uses the display face");
        Assert.That(m[10], Is.EqualTo("32px"), "the preview initial is 32 px");

        // Pale swatches switch the initial to navy ink for contrast (and the
        // selector script must keep that in sync, not only the background).
        await Page.Locator(".profile-color-swatch[data-color='#f59e0b']").ClickAsync();
        await Expect(Page.Locator("[data-color-preview]"))
            .ToHaveCSSAsync("color", "rgb(0, 25, 87)");

        // The custom colour input is invisible but still keyboard-reachable.
        await Page.Locator("[data-color-custom]").FocusAsync();
        await Expect(Page.Locator(".profile-color-custom"))
            .ToHaveCSSAsync("outline-color", "rgb(252, 93, 0)");
    }

    [Test]
    public async Task ProfileForm_UsesMetroUnderlineInputs()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles/Create");

        await Expect(Page.Locator(".page-head .metro-title")).ToHaveTextAsync("new profile");

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const input = document.querySelector('.profile-form input[name=Name]');
            const label = document.querySelector('.profile-form label[for=Name]');
            const inputStyle = getComputedStyle(input);
            const labelStyle = getComputedStyle(label);
            return [inputStyle.borderBottomWidth, inputStyle.borderTopWidth,
                    inputStyle.borderBottomLeftRadius, inputStyle.backgroundColor,
                    labelStyle.textTransform, labelStyle.fontSize, labelStyle.fontWeight];
        }");
        Assert.That(m[0], Is.EqualTo("2px"), "fields sit on a 2 px rule");
        Assert.That(m[1], Is.EqualTo("0px"), "fields have no box border");
        Assert.That(m[2], Is.EqualTo("0px"), "fields are square");
        Assert.That(m[3], Is.EqualTo("rgb(255, 255, 255)"), "fields sit on the field surface");
        Assert.That(m[4], Is.EqualTo("uppercase"), "labels are uppercase");
        Assert.That(m[5], Is.EqualTo("11px"), "labels are 11 px");
        Assert.That(m[6], Is.EqualTo("700"), "labels are bold");
    }

    [Test]
    public async Task ProfileValidationErrorIsReadableInDarkMode()
    {
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_theme", Value = "dark", Url = _factory.ServerAddress }
        });
        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles/Create");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark",
            new() { Timeout = 5000 });

        // Bypass the browser's required-field bubble so the server-side validation
        // message renders, then verify its dark-mode contrast.
        await Page.EvaluateAsync(@"() => {
            const form = document.querySelector('.profile-form');
            form.noValidate = true;
            form.requestSubmit();
        }");

        var error = Page.Locator("span[data-valmsg-for='Name']");
        await Expect(error).Not.ToBeEmptyAsync(new() { Timeout = 15000 });
        var m = await error.EvaluateAsync<string[]>(@"el => {
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
            return [style.color, contrast.toFixed(2)];
        }");
        Assert.That(m[0], Is.EqualTo("rgb(234, 134, 143)"), "validation text uses the dark danger emphasis token");
        Assert.That(double.Parse(m[1], System.Globalization.CultureInfo.InvariantCulture),
            Is.GreaterThanOrEqualTo(4.5), "the validation error meets AA contrast in dark mode");
    }

    [Test]
    public async Task Edit_UsesTwoColumnLayoutAndStationTiles()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles/Edit/{_profileId}");

        var form = Page.Locator(".profile-form--wide");
        await Expect(form).ToBeVisibleAsync();

        var tile = Page.Locator($".profile-station-tile:has(#station_{_stationId})");
        await Expect(tile).ToBeVisibleAsync();
        var checkbox = tile.Locator("input.btn-check");
        await Expect(checkbox).ToBeCheckedAsync();
        var label = tile.Locator("label");
        await Expect(label).ToHaveCountAsync(1);

        // btn-check + label hook: the checkbox id must match the label's for attribute.
        var hooks = await Page.EvaluateAsync<string[]>(@"() => {
            const tile = document.querySelector('.profile-station-tile');
            const input = tile.querySelector('input.btn-check');
            const label = tile.querySelector('label');
            return [input.id, label.getAttribute('for')];
        }");
        Assert.That(hooks[0], Is.Not.Empty);
        Assert.That(hooks[1], Is.EqualTo(hooks[0]));

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const form = document.querySelector('.profile-form--wide');
            const tile = document.querySelector('.profile-station-tile');
            const label = tile.querySelector('label');
            const mosaic = tile.querySelector('.collage-container');
            const check = tile.querySelector('.station-check-overlay');
            const name = tile.querySelector('.profile-station-tile__name');
            return [getComputedStyle(form).display,
                    getComputedStyle(form).gridTemplateColumns,
                    getComputedStyle(mosaic).display,
                    getComputedStyle(label).boxShadow,
                    getComputedStyle(check).width, getComputedStyle(check).height,
                    getComputedStyle(check).borderTopLeftRadius,
                    getComputedStyle(check).backgroundColor,
                    getComputedStyle(check).opacity,
                    getComputedStyle(name).fontFamily, getComputedStyle(name).fontSize];
        }");
        Assert.That(m[0], Is.EqualTo("grid"), "the edit form is a grid");
        Assert.That(m[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.GreaterThanOrEqualTo(2),
            "the edit form has two columns on desktop");
        Assert.That(m[2], Is.EqualTo("grid"), "the station tile reuses the mosaic");
        Assert.That(m[3], Does.Contain("inset"), "a checked station gets an inset ring");
        Assert.That(m[3], Does.Contain("rgb(252, 93, 0)"), "the checked ring is orange");
        Assert.That(m[4], Is.EqualTo("24px"), "the check square is 24 px wide");
        Assert.That(m[5], Is.EqualTo("24px"), "the check square is 24 px tall");
        Assert.That(m[6], Is.EqualTo("0px"), "the check square is square");
        Assert.That(m[7], Is.EqualTo("rgb(252, 93, 0)"), "the check square is orange");
        Assert.That(m[8], Is.EqualTo("1"), "the check is visible for a checked station");
        Assert.That(m[9], Does.Contain("Sulphur"), "the tile title uses the display face");
        Assert.That(double.Parse(m[10].Replace("px", string.Empty)), Is.GreaterThanOrEqualTo(18),
            "the tile title is at least 18 px");
    }

    [Test]
    public async Task Profiles_Fit390WithoutPageOverflow()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles");

        await Expect(Page.Locator(".profile-card").First).ToBeVisibleAsync(new() { Timeout = 15000 });

        var profilePage = await Page.EvaluateAsync<string[]>(@"() => {
            const card = document.querySelector('.profile-card');
            const grid = document.querySelector('.profile-grid');
            return [document.documentElement.scrollWidth.toString(),
                    window.innerWidth.toString(),
                    getComputedStyle(grid).gridTemplateColumns,
                    card.getBoundingClientRect().right.toString()];
        }");
        Assert.That(int.Parse(profilePage[0]), Is.LessThanOrEqualTo(int.Parse(profilePage[1]) + 1),
            "the profile page does not scroll sideways at 390 px");
        Assert.That(profilePage[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(1),
            "profile cards stack in one column at 390 px");
        Assert.That(double.Parse(profilePage[3]), Is.LessThanOrEqualTo(double.Parse(profilePage[1])),
            "the profile card stays inside the viewport");

        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles/Edit/{_profileId}");
        await Expect(Page.Locator(".profile-form--wide")).ToBeVisibleAsync();

        var editPage = await Page.EvaluateAsync<string[]>(@"() => {
            const form = document.querySelector('.profile-form--wide');
            return [document.documentElement.scrollWidth.toString(),
                    window.innerWidth.toString(),
                    getComputedStyle(form).gridTemplateColumns];
        }");
        Assert.That(int.Parse(editPage[0]), Is.LessThanOrEqualTo(int.Parse(editPage[1]) + 1),
            "the edit page does not scroll sideways at 390 px");
        Assert.That(editPage[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(1),
            "the edit form collapses to one column at 390 px");
    }
}
