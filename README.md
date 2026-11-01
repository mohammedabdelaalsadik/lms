# Training Platform (LMS)

A Moodle-style learning management system: ASP.NET Core 10 controllers, SQL Server and Angular 22,
built as a modular monolith with Clean Architecture and DDD. The product requirements live in the
PRD document.

## Structure

```
backend/
  src/BuildingBlocks/   Shared kernel, CQRS dispatcher, outbox, base controller
  src/Modules/<Name>/   One bounded context
    Domain              Aggregates, value objects, events, business rules. No frameworks.
    Application         Commands, queries, validators, ports
    Infrastructure      EF Core, repositories, security, module registration
    Presentation        Controllers
  src/Host/Lms.Api/     Composition root
  tests/                Domain tests and architecture tests
frontend/
  src/app/core/         Auth, i18n, theme, HTTP
  src/app/shared/       Reusable UI
  src/app/layout/       Application shell
  src/app/features/     One folder per business area, lazy loaded
  public/i18n/          Arabic and English text
```

Dependencies point inward: Presentation and Infrastructure depend on Application, which depends on
Domain. Modules never reference each other, and each owns a SQL schema. `Lms.ArchitectureTests` fails
the build's tests if these rules break.

## Run locally

Requires .NET SDK 10, Node 24 and a local SQL Server (default instance, Windows authentication).

```
dotnet user-secrets list --project backend/src/Host/Lms.Api   # shows the dev admin login
dotnet run --project backend/src/Host/Lms.Api --launch-profile http
npm --prefix frontend start
```

- App: http://localhost:4200
- API reference: http://localhost:5235/scalar
- In Development the API applies migrations and seeds made-up sample data into `MoodleLmsDev`.
- Uploaded files go to the folder set by `Storage:Path`, outside the web root. It has no default outside Development, so production must set it.
- Production must also set `Certification:VerifyBaseUrl`, the public site address printed in certificate QR codes. Linux hosts need the `SkiaSharp.NativeAssets.Linux` and `HarfBuzzSharp.NativeAssets.Linux` packages.
- Behind a reverse proxy, production must set `Assessment:Seb:BaseUrl` to the public site address, or Safe Exam Browser checks fail.
- Live classes need `LiveClasses:BaseUrl` (ending in `/bigbluebutton/api/`) and `LiveClasses:Secret`, kept in secrets. Optional: `LiveClasses:ChecksumAlgorithm` (`sha256` by default, `sha1` for older servers) and `LiveClasses:ReturnUrl`.
- Email is `Pickup` mode in Development: messages land as `.eml` files in `App_Data/mail`. Production sets `Email:Mode=Smtp`, `Email:Host`, `Email:Port`, `Email:UseSsl`, `Email:UserName`, `Email:Password`, `Email:From` and `Email:AppBaseUrl`.
- Setting `Seed:DemoPassword` in user secrets creates made-up demo accounts for every role, in Development only.

On a new machine, set these user secrets. The three keys are each base64 of 32 random bytes:
`Identity:Jwt:SigningKey`, `Identity:DataProtection:EncryptionKey`, `Identity:DataProtection:LookupKey`,
plus `Seed:AdminNationalId` and `Seed:AdminPassword`.

## Add a migration

```
cd backend
dotnet ef migrations add <Name> \
  --project src/Modules/Catalog/Lms.Modules.Catalog.Infrastructure \
  --startup-project src/Host/Lms.Api --context CatalogDbContext --output-dir Migrations
```

## Test

```
dotnet test backend/Lms.slnx
npm --prefix frontend test -- --watch=false
```

## Rules for this codebase

- HTTP endpoints are controllers. Minimal APIs are not used.
- No MediatR and no Keycloak. `IDispatcher` runs commands and queries; the API issues its own JWTs.
- Sign-in uses a 15-minute JWT and a rotating 14-day refresh token, both in HttpOnly cookies.
- Modules call each other only through their `Contracts` project, never their internals.
- Course content opens on the cohort's start date. Sequential locks are enforced by the API, including on file downloads.
- Attendance counts against planned sessions, less excused ones, so a course can complete as soon as its rule is met.
- Quizzes and assignments belong to a cohort. The best quiz attempt counts, and the server's clock ends every attempt.
- Grades stay hidden until their item is published. After that, an existing grade changes only through a correction approved by a second person.
- Certificates hold a snapshot of names and are never edited: a wrong one is revoked and reissued with a new code. The PDF is generated on request from that snapshot.
- Background work reads the outbox and must be safe to repeat, because a failed message is retried. A stored event is marked done once, so each event type has exactly one processor; if two things must react, one processor does both.
- A forum belongs to one cohort. Reach it only through `ForumAccess`, which checks membership on every call. Removed text never leaves the server.
- Who may message whom is decided by `MessagingPolicy`. Message text stays out of notifications, email and the audit log.
- Every outbox event type needs its own processor.
- Staff must use two-step sign-in when `Identity:Mfa:RequiredForStaff` is true, which is the production default. Development sets it to false.
- A quiz copies questions from the bank, so bank edits never reach a published quiz. Marking is all or nothing per question.
- Trainee attempt responses must never carry accepted answers, `IsCorrect` or matching pairs. The attempt `Seed` stays on the server.
- Bulk upload never creates administrators. Uploaded accounts must change their password before the API opens.
- H5P uses the same sandbox and signed links. Its player files live in `wwwroot/h5p-player` (h5p-standalone, MIT) and load from the app, never a CDN.
- SCORM packages stay zipped and run only in a sandboxed frame with no origin. Never add `allow-same-origin`. The signed link is their only permission.
- Secrets such as reset links go through `IDirectMailer`, never `INotifier`, because notifications are stored.
- Exports use `CsvWriter` and mark their query `IAuditedRequest`. National IDs stay masked.
- A trainee belongs to at most one group per cohort. Forum handlers must check `Membership.Sees` for every topic they touch.
- Badges are awarded in the same save as the certificate. Revoking the certificate removes them and reissuing restores them.
- Competency attainment is worked out on every read from valid certificates and staff sign-offs. It is never stored, so revoking a certificate takes effect at once.
- Removing a section or activity moves it to the recycle bin with its original ids, and its files stay until the item is purged after 30 days. Only `RecycleBinPurge` and `PurgeBinItemCommand` may delete those files.
- A course backup holds course details, content, files and the question bank, including accepted answers, so it stays staff-only and is audited. Restore always makes a new draft course and sends everything through the normal upload checks.
- A module that stores something about a person registers an `IPersonalDataSource`, and a new table with personal data must be added to it. Exports carry published grades only and no secrets.
- Erasure keeps the user row with its identity removed. Grades, attendance, certificates and the audit log stay as records. Modules erase first because those steps can repeat; the request closes with the account in one save.
- Choice, feedback and survey activities are all `ActivityKind.Form`. Questions never change after saving, because answers refer to them by position. An anonymous response stores no trainee and no time, and its id is random; who answered lives only in `FormSubmissions`. Anonymous results stay hidden below `AnonymousMinimum`.
- Books and lessons live in `Activity.StructureJson`. A lesson must have a route to its end, and its completion is the trainee's own click, like a page.
- A wiki save must name the version it started from, and every save adds a revision. Reach pages only through `WikiPages`, which checks the cohort. Wiki text stays after erasure, because it is shared work.
- Workshop reviews are anonymous: reviewers never receive an author's name, and authors never receive a reviewer's. Scores reach an author only once the gradebook item is published.
- Private files are reachable only by their owner, with no staff override. Erasure deletes them.
- Online users are held in memory for 5 minutes and never stored.
- A quiz with an `ExamKey` checks the Safe Exam Browser hash on start, save and submit. The key is staff-only: it must never reach a trainee response.
- Live classes reach BigBlueButton only through `ILiveClassServer`. A join link works as a password: it goes to its one owner, and is never stored or logged. Recording is off.
- Dashboard charts are hand-built in `shared/charts.ts` with no chart library. One measure per chart, never two scales, and every value is printed or in a table view. Only Admin and Auditor receive user numbers.
- Search returns only what the person could already open elsewhere.
- A `HasMany` with a shadow foreign key needs `IsRequired()`. Otherwise removing a child only unlinks its row and leaves it in the table.
- Keys of owned collections assigned by our code need `ValueGeneratedNever`, or EF treats new rows as updates.
- To notify someone, call `INotifier` with a unique `DedupeKey`. Wording lives in `NotificationTemplates`, in both languages. Keep grades and National IDs out of notifications.
- Every command is audited automatically in the dispatcher. Mark a sensitive read with `IAuditedRequest`, and routine high-volume commands with `INotAudited`. Details use an allow-list, so new text fields never reach the log unless added on purpose.
- The audit table is append-only through a database trigger. A full National ID is returned only with a written reason, and only if its audit entry was saved.
- A final grade exists only when every item is published and graded. Assessment pushes it to Learning, which applies the pass mark.
- Business rules live in the Domain layer. Expected failures return `Result`, not exceptions.
- Lists use `urlFilters` with `app-filter-bar`, so filters live in the URL. Dates use `app-date-field`, not the browser's own date input.
- Components re-render on signals and events only. A control that must react to form state, such as being marked touched, has to request it.
- Error codes are stable. Every new code needs a line in both `public/i18n` files.
- All user-facing text exists in Arabic and English. Layout uses logical CSS properties.
- National IDs are encrypted at rest, masked in responses, and never put in logs or domain events.
- Development and test data are always made up. Never copy production personal data here.

## Hidden features

Live classes are hidden until BigBlueButton is connected. To show them, set `LiveClasses:Enabled` to `true` on the API and `LIVE_CLASSES_ENABLED` to `true` in `frontend/src/app/core/features.ts`.
