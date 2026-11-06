using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Obrl.Web.Controllers;
using Obrl.Web.Data;
using Obrl.Web.Domain;
using Obrl.Web.Services;
using Obrl.Web.Services.Email;

namespace Obrl.Tests;

public class BookMetadataTests
{
    [Theory]
    [InlineData(1000, true)]
    [InlineData(1998, true)]
    [InlineData(2026, true)]
    [InlineData(2027, false)]
    [InlineData(999, false)]
    [InlineData(98, false)]
    [InlineData(0, false)]
    public void Publication_year_has_four_digits_and_is_not_in_the_future(int year, bool valid) =>
        Assert.Equal(valid, UploadsController.IsValidYear(year, thisYear: 2026));

    [Fact]
    public void Publisher_names_match_ignoring_case_and_spaces()
    {
        Assert.Equal(Publisher.Normalize("Visalaandhra  Publishing House"), Publisher.Normalize(" visalaandhra publishing house "));
        Assert.NotEqual(Publisher.Normalize("Emesco"), Publisher.Normalize("Emesco Books"));
    }
}

public class EmailTests
{
    [Fact]
    public void Mail_text_is_html_encoded()
    {
        var mail = new MailBody()
            .Paragraph("Hello <b>Ravi</b> & co,")
            .Field("Temporary password", "a<b\"c", monospace: true)
            .Button("Sign in", "https://books.example.org/Identity/Account/Login?reason=welcome&x=1")
            .ToMail("ravi@example.org", "Ravi", "Welcome");
        Assert.Contains("Hello &lt;b&gt;Ravi&lt;/b&gt; &amp; co,", mail.Html);
        Assert.Contains("a&lt;b&quot;c", mail.Html);
        Assert.Contains("reason=welcome&amp;x=1", mail.Html);
        Assert.DoesNotContain("<b>Ravi", mail.Html);
        // The plain-text part keeps the text as typed.
        Assert.Contains("Hello <b>Ravi</b> & co,", mail.Text);
        Assert.Contains("Temporary password: a<b\"c", mail.Text);
    }

    [Fact]
    public void Temporary_passwords_are_long_random_and_unambiguous()
    {
        var a = Notifications.NewTemporaryPassword();
        var b = Notifications.NewTemporaryPassword();
        Assert.Equal(16, a.Length);
        Assert.NotEqual(a, b);
        Assert.DoesNotContain(a, c => "0O1lI".Contains(c));
    }

    [Theory]
    [InlineData("Auto", 587, MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData("Auto", 465, MailKit.Security.SecureSocketOptions.SslOnConnect)]
    [InlineData("StartTls", 587, MailKit.Security.SecureSocketOptions.StartTls)]
    [InlineData("SslOnConnect", 465, MailKit.Security.SecureSocketOptions.SslOnConnect)]
    [InlineData("None", 25, MailKit.Security.SecureSocketOptions.None)]
    public void Connection_security_follows_the_setting(string security, int port, MailKit.Security.SecureSocketOptions expected) =>
        Assert.Equal(expected, Mailer.SecurityFor(new EmailOptions { Security = security, Port = port }));

    [Fact]
    public void Email_is_off_until_server_and_sender_are_set()
    {
        Assert.False(new EmailOptions().IsConfigured);
        Assert.False(new EmailOptions { Host = "smtp.example.org" }.IsConfigured);
        Assert.True(new EmailOptions { Host = "smtp.example.org", FromAddress = "library@example.org" }.IsConfigured);
    }

    [Fact]
    public async Task Email_choices_default_on_and_are_saved()
    {
        var options = new DbContextOptionsBuilder<ObrlDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new FakeTimeProvider();
        using (var db = new ObrlDbContext(options))
        {
            var settings = new EmailSettings(clock);
            await settings.LoadAsync(db);
            Assert.True(settings.NotifyUploads);
            Assert.True(settings.WelcomeUsers);
            await settings.SaveAsync(db, notifyUploads: false, welcomeUsers: true, adminId: "admin");
        }
        using (var db = new ObrlDbContext(options))
        {
            var reloaded = new EmailSettings(clock);
            await reloaded.LoadAsync(db);
            Assert.False(reloaded.NotifyUploads);
            Assert.True(reloaded.WelcomeUsers);
        }
    }
}
