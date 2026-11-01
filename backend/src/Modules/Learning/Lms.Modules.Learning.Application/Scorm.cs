using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Lms.Modules.Learning.Application;

public sealed record ScormLaunch(Guid ActivityId, Guid CohortId, Guid TraineeId);

/// <summary>Signed, short-lived links. They are the only thing a sandboxed package holds.</summary>
public interface IScormTokens
{
    string Issue(ScormLaunch launch);

    ScormLaunch? Read(string token);
}

public interface IScormAttemptRepository
{
    Task<ScormAttempt?> FindAsync(ActivityId activityId, Guid cohortId, Guid traineeId, CancellationToken cancellationToken);

    void Add(ScormAttempt attempt);
}

/// <summary>Checks a package without unpacking it. Packages stay zipped, so no name in one can touch the disk.</summary>
public static class ScormPackage
{
    public const long MaxSize = 300 * 1024 * 1024;
    public const long MaxUnpackedSize = 1024L * 1024 * 1024;
    private const int MaxEntries = 20_000;

    public sealed record Info(string EntryPoint);

    public static Info? Inspect(Stream content)
    {
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

            if (zip.Entries.Count > MaxEntries || zip.Entries.Sum(e => e.Length) > MaxUnpackedSize)
            {
                return null;
            }

            var manifest = zip.GetEntry("imsmanifest.xml");
            if (manifest is null)
            {
                return null;
            }

            using var stream = manifest.Open();
            var entry = ReadEntryPoint(stream);
            var file = entry?.Split('?', '#')[0];

            return file is not null && Find(zip, file) is not null ? new Info(entry!) : null;
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or NotSupportedException or IOException or ArgumentException)
        {
            return null;
        }
    }

    public static ZipArchiveEntry? Find(ZipArchive zip, string path)
    {
        var name = path.Replace('\\', '/').TrimStart('/');
        return zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadEntryPoint(Stream manifest)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 };
        using var reader = XmlReader.Create(manifest, settings);
        var document = XDocument.Load(reader);

        IEnumerable<XElement> Named(XContainer parent, string name) => parent.Descendants().Where(e => e.Name.LocalName == name);

        var organizations = Named(document, "organizations").FirstOrDefault();
        var preferred = organizations?.Attribute("default")?.Value;
        var organization = Named(document, "organization").FirstOrDefault(o => o.Attribute("identifier")?.Value == preferred) ?? Named(document, "organization").FirstOrDefault();
        var reference = organization is null ? null : Named(organization, "item").Select(i => i.Attribute("identifierref")?.Value).FirstOrDefault(v => v is not null);
        var resources = Named(document, "resource").ToList();
        var resource = resources.Find(r => r.Attribute("identifier")?.Value == reference) ?? resources.Find(r => r.Attribute("href") is not null);
        var href = resource?.Attribute("href")?.Value;

        if (href is null)
        {
            return null;
        }

        var parameters = Named(document, "item").FirstOrDefault(i => i.Attribute("identifierref")?.Value == reference)?.Attribute("parameters")?.Value;
        return string.IsNullOrWhiteSpace(parameters) ? href : $"{href}?{parameters.TrimStart('?')}";
    }
}

/// <summary>Reads the file every .h5p carries. Libraries travel inside the package.</summary>
public static class H5pPackage
{
    public const string PlayerPage = "__player.html";
    public const string PlayerFiles = "__h5p/";

    public static bool IsValid(Stream content)
    {
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

            if (zip.Entries.Sum(e => e.Length) > ScormPackage.MaxUnpackedSize || ScormPackage.Find(zip, "h5p.json") is not { } entry || ScormPackage.Find(zip, "content/content.json") is null)
            {
                return false;
            }

            using var stream = entry.Open();
            using var json = JsonDocument.Parse(stream);
            return json.RootElement.TryGetProperty("mainLibrary", out _);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or NotSupportedException or IOException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Runs inside the same sandbox as a SCORM package, from files shipped with the app.</summary>
    public static string Page(string token) => """
        <!doctype html><html><head><meta charset="utf-8"><title>H5P</title>
        <link rel="stylesheet" href="__h5p/styles/h5p.css"><script>(function(){function memory(){var s={};return {getItem:function(k){return k in s?s[k]:null;},setItem:function(k,v){s[k]=String(v);},removeItem:function(k){delete s[k];},clear:function(){s={};},key:function(i){return Object.keys(s)[i]||null;},get length(){return Object.keys(s).length;}};}
        ["localStorage","sessionStorage"].forEach(function(n){Object.defineProperty(window,n,{value:memory(),configurable:true});});})();</script><script src="__h5p/main.bundle.js"></script></head>
        <body><div id="h5p"></div><script>
        (function(){
          var base="/api/v1/scorm/__TOKEN__",sent=false;
          function report(data){fetch(base+"/commit",{method:"POST",mode:"no-cors",keepalive:true,headers:{"Content-Type":"text/plain"},body:JSON.stringify(data)})
            .finally(function(){try{window.top.postMessage({type:"lms-scorm-saved"},"*");}catch(e){}});}
          new H5PStandalone.H5P(document.getElementById("h5p"),{h5pJsonPath:base,librariesPath:base,frameJs:base+"/__h5p/frame.bundle.js",frameCss:base+"/__h5p/styles/h5p.css",embedType:"div"}).then(function(){
            H5P.externalDispatcher.on("xAPI",function(event){
              var verb=event.getVerb(),parent=event.data.statement.context&&event.data.statement.context.contextActivities&&event.data.statement.context.contextActivities.parent;
              if(!sent&&!parent&&(verb==="completed"||verb==="answered"||verb==="passed")){sent=true;report({"cmi.completion_status":"completed"});}
            });
          });
        })();
        </script></body></html>
        """.Replace("__TOKEN__", token, StringComparison.Ordinal);
}

public sealed record AddScormActivityCommand(
    ActivityKind Kind,
    Guid CourseId,
    Guid SectionId,
    string TitleAr,
    string TitleEn,
    bool IsRequired,
    string FileName,
    long Size,
    Stream Content) : ICommand<CourseContentResponse>;

internal sealed class AddScormActivityValidator : AbstractValidator<AddScormActivityCommand>
{
    public AddScormActivityValidator()
    {
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Activity.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Activity.TitleMaxLength);
    }
}

internal sealed class AddScormActivityHandler(ContentEditor editor, IFileStorage storage) : IRequestHandler<AddScormActivityCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(AddScormActivityCommand request, CancellationToken cancellationToken)
    {
        if (request.Size is <= 0 or > ScormPackage.MaxSize)
        {
            return FileErrors.TooLarge;
        }

        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Activity.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return title.Error!;
        }

        var entryPoint = request.Kind == ActivityKind.H5p
            ? (H5pPackage.IsValid(request.Content) ? H5pPackage.PlayerPage : null)
            : ScormPackage.Inspect(request.Content)?.EntryPoint;

        if (entryPoint is null)
        {
            return ContentErrors.InvalidScormPackage;
        }

        request.Content.Position = 0;
        var key = await storage.SaveAsync(request.Content, cancellationToken);
        var name = Path.GetFileName(request.FileName);
        var asset = new FileAsset(key, name.Length > 255 ? name[^255..] : name, "application/zip", request.Size);
        var outcome = content.Value.AddPackage(request.Kind, new SectionId(request.SectionId), title.Value, request.IsRequired, asset, entryPoint);

        if (!outcome.IsSuccess)
        {
            storage.Delete(key);
        }

        return await editor.FinishAsync(content.Value, outcome, cancellationToken);
    }
}

public sealed record ScormLaunchResponse(string Url);

public sealed record LaunchScormCommand(Guid CohortId, Guid ActivityId) : ICommand<ScormLaunchResponse>;

internal sealed class LaunchScormHandler(TraineeGate gate, ICurrentUser user, ICourseContentRepository contents, IScormTokens tokens)
    : IRequestHandler<LaunchScormCommand, ScormLaunchResponse>
{
    public async Task<Result<ScormLaunchResponse>> Handle(LaunchScormCommand request, CancellationToken cancellationToken)
    {
        var cohort = await gate.EnterAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var content = await contents.FindByCourseAsync(cohort.Value.CourseId, cancellationToken);
        var activity = content?.FindActivity(new ActivityId(request.ActivityId));

        if (activity is not { Kind: ActivityKind.Scorm or ActivityKind.H5p, ScormEntry: not null })
        {
            return ContentErrors.ActivityNotFound;
        }

        var token = tokens.Issue(new ScormLaunch(request.ActivityId, request.CohortId, user.Id!.Value));
        var parts = activity.ScormEntry.Split('?', 2);
        var path = string.Join('/', parts[0].Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        return new ScormLaunchResponse($"/api/v1/scorm/{token}/{path}{(parts.Length > 1 ? $"?{parts[1]}" : "")}");
    }
}

public sealed record ScormAsset(Stream Content, string ContentType, IDisposable Owner);

public sealed record ReadScormAssetQuery(string Token, string Path) : IQuery<ScormAsset>, INotAudited;

/// <summary>Serves one file out of the zip. Pages also get the small SCORM runtime the package talks to.</summary>
internal sealed class ReadScormAssetHandler(
    IScormTokens tokens,
    ICourseContentRepository contents,
    IScormAttemptRepository attempts,
    IFileStorage storage) : IRequestHandler<ReadScormAssetQuery, ScormAsset>
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".htm"] = "text/html; charset=utf-8", [".js"] = "text/javascript", [".css"] = "text/css",
        [".json"] = "application/json", [".xml"] = "application/xml", [".svg"] = "image/svg+xml", [".png"] = "image/png", [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp", [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav", [".woff"] = "font/woff", [".woff2"] = "font/woff2", [".ttf"] = "font/ttf", [".vtt"] = "text/vtt",
    };

    public async Task<Result<ScormAsset>> Handle(ReadScormAssetQuery request, CancellationToken cancellationToken)
    {
        var launch = tokens.Read(request.Token);
        var content = launch is null ? null : await contents.FindByActivityAsync(new ActivityId(launch.ActivityId), cancellationToken);
        var activity = launch is null ? null : content?.FindActivity(new ActivityId(launch.ActivityId));

        if (launch is null || activity is not { Kind: ActivityKind.Scorm or ActivityKind.H5p, FileKey: not null })
        {
            return ContentErrors.ActivityNotFound;
        }

        if (activity.Kind == ActivityKind.H5p && request.Path == H5pPackage.PlayerPage)
        {
            return new ScormAsset(new MemoryStream(Encoding.UTF8.GetBytes(H5pPackage.Page(request.Token))), "text/html; charset=utf-8", new MemoryStream());
        }

        var zip = new ZipArchive(storage.Open(activity.FileKey), ZipArchiveMode.Read);
        var entry = ScormPackage.Find(zip, request.Path);

        if (entry is null)
        {
            zip.Dispose();
            return ContentErrors.ActivityNotFound;
        }

        var type = Types.GetValueOrDefault(Path.GetExtension(entry.Name), "application/octet-stream");

        if (!type.StartsWith("text/html", StringComparison.Ordinal))
        {
            return new ScormAsset(entry.Open(), type, zip);
        }

        if (activity.Kind == ActivityKind.H5p)
        {
            return new ScormAsset(entry.Open(), "text/plain", zip);
        }

        var saved = await attempts.FindAsync(new ActivityId(launch.ActivityId), launch.CohortId, launch.TraineeId, cancellationToken);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var html = await reader.ReadToEndAsync(cancellationToken);
        zip.Dispose();

        var runtime = ScormRuntime.Script(saved?.Data, $"/api/v1/scorm/{request.Token}/commit");
        var at = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        var close = at < 0 ? -1 : html.IndexOf('>', at);
        var page = close < 0 ? runtime + html : html.Insert(close + 1, runtime);

        return new ScormAsset(new MemoryStream(Encoding.UTF8.GetBytes(page)), type, new MemoryStream());
    }
}

public static class ScormRuntime
{
    private const string Template = """
        <script>(function(){function memory(){var s={};return {getItem:function(k){return k in s?s[k]:null;},setItem:function(k,v){s[k]=String(v);},removeItem:function(k){delete s[k];},clear:function(){s={};},key:function(i){return Object.keys(s)[i]||null;},get length(){return Object.keys(s).length;}};}
        ["localStorage","sessionStorage"].forEach(function(n){Object.defineProperty(window,n,{value:memory(),configurable:true});});})();</script><script>(function(){
        var d=__DATA__,u=__URL__,dirty=false,finished=false;
        function get(k){return Object.prototype.hasOwnProperty.call(d,k)?String(d[k]):"";}
        function set(k,v){d[k]=String(v);dirty=true;return "true";}
        var queue=Promise.resolve();
        function send(body){return fetch(u,{method:"POST",mode:"no-cors",keepalive:body.length<60000,headers:{"Content-Type":"text/plain"},body:body});}
        function save(){if(!dirty)return "true";dirty=false;var body=JSON.stringify(d);
          queue=queue.then(function(){return send(body);}).catch(function(){dirty=true;})
          .then(function(){try{window.top.postMessage({type:"lms-scorm-saved"},"*");}catch(e){}});return "true";}
        function leaving(){if(dirty){dirty=false;send(JSON.stringify(d));}}
        function end(){if(finished)return "true";finished=true;dirty=true;return save();}
        function ok(){return "true";}function no(){return "0";}function blank(){return "";}
        window.API={LMSInitialize:ok,LMSFinish:end,LMSGetValue:get,LMSSetValue:set,LMSCommit:save,LMSGetLastError:no,LMSGetErrorString:blank,LMSGetDiagnostic:blank};
        window.API_1484_11={Initialize:ok,Terminate:end,GetValue:get,SetValue:set,Commit:save,GetLastError:no,GetErrorString:blank,GetDiagnostic:blank};
        window.addEventListener("pagehide",leaving);
        })();</script>
        """;

    public static string Script(string? saved, string commitUrl)
    {
        var data = string.IsNullOrEmpty(saved) ? "{}" : saved;

        // The default encoder escapes angle brackets, so saved text cannot close the script.
        var safe = JsonSerializer.Serialize(JsonSerializer.Deserialize<Dictionary<string, string>>(data));
        return Template.Replace("__DATA__", safe, StringComparison.Ordinal).Replace("__URL__", JsonSerializer.Serialize(commitUrl), StringComparison.Ordinal);
    }
}

public sealed record CommitScormCommand(string Token, string Body) : ICommand<Unit>, INotAudited
{
    public override string ToString() => nameof(CommitScormCommand);
}

internal sealed class CommitScormHandler(
    IScormTokens tokens,
    ICohortDirectory cohorts,
    IScormAttemptRepository attempts,
    ActivityCompleter completer,
    ILearningUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<CommitScormCommand, Unit>
{
    public const int MaxBody = 256 * 1024;

    public async Task<Result<Unit>> Handle(CommitScormCommand request, CancellationToken cancellationToken)
    {
        var launch = tokens.Read(request.Token);
        if (launch is null || !await cohorts.IsEnrolledAsync(launch.CohortId, launch.TraineeId, cancellationToken))
        {
            return ContentErrors.ActivityNotFound;
        }

        Dictionary<string, string>? values;

        try
        {
            values = request.Body.Length > MaxBody ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body);
        }
        catch (JsonException)
        {
            values = null;
        }

        if (values is null)
        {
            return ContentErrors.InvalidScormPackage;
        }

        var activityId = new ActivityId(launch.ActivityId);
        var attempt = await attempts.FindAsync(activityId, launch.CohortId, launch.TraineeId, cancellationToken);
        var now = clock.GetUtcNow();

        if (attempt is null)
        {
            attempt = ScormAttempt.Start(activityId, launch.CohortId, launch.TraineeId, now);
            attempts.Add(attempt);
        }

        attempt.Save(request.Body, ScormResult.IsComplete(values), now);

        if (attempt.CompletedAt is not null && await cohorts.FindAsync(launch.CohortId, cancellationToken) is { } cohort)
        {
            await completer.CompleteAsync(cohort, launch.TraineeId, activityId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record CheckScormLinkQuery(string Token) : IQuery<Unit>, INotAudited;

internal sealed class CheckScormLinkHandler(IScormTokens tokens) : IRequestHandler<CheckScormLinkQuery, Unit>
{
    public Task<Result<Unit>> Handle(CheckScormLinkQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<Unit>>(tokens.Read(request.Token) is null ? ContentErrors.ActivityNotFound : default(Unit));
}
