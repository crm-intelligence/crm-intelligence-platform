using System.Text;

namespace CrmAnalytics.Teams.Hosting;

public static class PublicPageEndpointExtensions
{
    private const string HtmlContentType = "text/html; charset=utf-8";

    public static IEndpointRouteBuilder MapCrmAnalyticsPublicPages(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/", () => Html(HomeHtml));
        endpoints.MapGet("/privacy", () => Html(PrivacyHtml));
        endpoints.MapGet("/terms", () => Html(TermsHtml));

        return endpoints;
    }

    private static IResult Html(string content) =>
        Results.Content(content, HtmlContentType, Encoding.UTF8);

    private const string SharedStyles = """
        :root { color-scheme: light dark; font-family: system-ui, sans-serif; line-height: 1.6; }
        body { max-width: 48rem; margin: 0 auto; padding: 2rem 1.25rem; }
        header, main, footer { display: block; }
        nav a { margin-right: 1rem; }
        h1, h2 { line-height: 1.2; }
        footer { margin-top: 2.5rem; font-size: .9rem; }
        """;

    private const string HomeHtml = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>CRM Analytics Pilot</title>
          <style>
        """ + SharedStyles + """
          </style>
        </head>
        <body>
          <header>
            <h1>CRM Analytics Pilot</h1>
            <nav aria-label="Legal links">
              <a href="/privacy">Privacy</a>
              <a href="/terms">Terms</a>
            </nav>
          </header>
          <main>
            <p>This application creates authorized CRM and Microsoft Fabric analytics reports through Microsoft Teams.</p>
            <p>It is an internal organizational pilot intended only for approved users and approved business analytics purposes.</p>
          </main>
          <footer>This pilot information does not replace a production legal review.</footer>
        </body>
        </html>
        """;

    private const string PrivacyHtml = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Privacy - CRM Analytics Pilot</title>
          <style>
        """ + SharedStyles + """
          </style>
        </head>
        <body>
          <header>
            <h1>Privacy</h1>
            <nav aria-label="Site links"><a href="/">Home</a><a href="/terms">Terms</a></nav>
          </header>
          <main>
            <p>CRM Analytics Pilot may process Microsoft Teams user and tenant identifiers, a conversation identifier, the report request, and security-focused audit metadata.</p>
            <p>Credentials such as SQL connection strings, access tokens, and client secrets are not shown to users. Data is processed only for authorized organizational analytics purposes.</p>
            <p>User-supplied parameter values are not written to the new audit metadata field.</p>
            <p>This application is an internal pilot. Data access and retention policies are managed by the organization&#39;s administrators.</p>
          </main>
          <footer>This pilot notice does not replace a production privacy or legal review.</footer>
        </body>
        </html>
        """;

    private const string TermsHtml = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Terms - CRM Analytics Pilot</title>
          <style>
        """ + SharedStyles + """
          </style>
        </head>
        <body>
          <header>
            <h1>Terms of Use</h1>
            <nav aria-label="Site links"><a href="/">Home</a><a href="/privacy">Privacy</a></nav>
          </header>
          <main>
            <ul>
              <li>Use is limited to authorized organizational purposes.</li>
              <li>Users must not attempt to exceed or bypass their access permissions.</li>
              <li>Generated analytics must be verified before they are used for a decision.</li>
              <li>The pilot service may be changed, suspended, or discontinued.</li>
              <li>Use of the application is subject to applicable organizational policies.</li>
            </ul>
          </main>
          <footer>These pilot terms do not replace a production legal review.</footer>
        </body>
        </html>
        """;
}
