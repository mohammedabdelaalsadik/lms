using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;
using System.Security.Cryptography;
using System.Text;

namespace Lms.Modules.Identity.Application;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<LoginResponse>
{
    public override string ToString() => nameof(ChangePasswordCommand);
}

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128);
    }
}

internal sealed class ChangePasswordHandler(
    IUserRepository users,
    IPasswordService passwords,
    IRefreshTokenRepository refreshTokens,
    SessionStarter sessions,
    ICurrentUser current,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<ChangePasswordCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(new UserId(current.Id!.Value), cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var now = clock.GetUtcNow();

        if (!passwords.Verify(user.PasswordHash, request.CurrentPassword))
        {
            user.RecordFailedLogin(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return UserErrors.WrongPassword;
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return UserErrors.SamePassword;
        }

        user.ChangePassword(passwords.Hash(request.NewPassword));

        // Anyone else using the old password is signed out here.
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken);
        var session = sessions.Start(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }
}

public sealed record UploadRow(int Line, string? MaskedNationalId, string? NameAr, string? NameEn, string? Email, string? Role, string? ErrorCode);

public sealed record UploadedUser(int Line, string Email, string TemporaryPassword);

public sealed record UserUploadResponse(int Valid, int Invalid, IReadOnlyList<UploadRow> Rows, IReadOnlyList<UploadedUser> Created);

/// <summary>Reads comma-separated text with quoted fields. A formula-looking cell is only text here.</summary>
public static class CsvReader
{
    public static IReadOnlyList<(int Line, string[] Cells)> Read(string text)
    {
        var rows = new List<(int, string[])>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var line = 1;
        var start = 1;

        void EndRow()
        {
            cells.Add(cell.ToString().Trim());
            cell.Clear();

            if (cells.Exists(c => c.Length > 0))
            {
                rows.Add((start, [.. cells]));
            }

            cells.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    line += c == '\n' ? 1 : 0;
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else if (c == '\n')
            {
                EndRow();
                line++;
                start = line;
            }
            else if (c != '\r')
            {
                cell.Append(c);
            }
        }

        EndRow();
        return rows;
    }
}

/// <summary>Checks a whole file first. Saving creates the valid rows and gives each person a one-time password.</summary>
public sealed record UploadUsersCommand(string Content, bool Save) : ICommand<UserUploadResponse>
{
    public override string ToString() => nameof(UploadUsersCommand);
}

internal sealed class UploadUsersHandler(
    IUserRepository users,
    INationalIdProtector protector,
    IPasswordService passwords,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<UploadUsersCommand, UserUploadResponse>
{
    private const int MaxRows = 1000;
    private const int PasswordLength = 14;
    private const string PasswordAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
    private static readonly string[] Header = ["national_id", "name_ar", "name_en", "email"];

    public async Task<Result<UserUploadResponse>> Handle(UploadUsersCommand request, CancellationToken cancellationToken)
    {
        var rows = CsvReader.Read(request.Content.TrimStart('﻿'));

        if (rows.Count == 0)
        {
            return UserErrors.UploadInvalid;
        }

        var header = rows[0].Cells.Select(h => h.ToLowerInvariant()).ToList();
        var columns = Header.Select(name => header.IndexOf(name)).ToArray();

        if (columns.Contains(-1))
        {
            return UserErrors.UploadInvalid;
        }

        if (rows.Count - 1 > MaxRows)
        {
            return UserErrors.UploadTooLarge;
        }

        var roleColumn = header.IndexOf("role");
        var seenIds = new HashSet<string>();
        var seenEmails = new HashSet<string>();
        var results = new List<UploadRow>();
        var created = new List<UploadedUser>();
        var now = clock.GetUtcNow();

        foreach (var (line, cells) in rows.Skip(1))
        {
            string Cell(int index) => index >= 0 && index < cells.Length ? cells[index] : "";

            var nationalId = NationalId.Create(Cell(columns[0]));
            var nameAr = Cell(columns[1]);
            var nameEn = Cell(columns[2]);
            var email = Cell(columns[3]);
            var masked = nationalId.IsSuccess ? $"******{nationalId.Value.Value[^4..]}" : null;
            var name = LocalizedText.Create(nameAr, nameEn, User.NameMaxLength);
            var hash = nationalId.IsSuccess ? protector.ComputeLookupHash(nationalId.Value) : "";
            var normalEmail = User.NormalizeEmail(email);
            var role = ReadRole(roleColumn < 0 ? "" : Cell(roleColumn));

            Error? problem = null;

            if (!nationalId.IsSuccess)
            {
                problem = UserErrors.InvalidNationalId;
            }
            else if (!name.IsSuccess)
            {
                problem = UserErrors.UploadName;
            }
            else if (!normalEmail.Contains('@') || normalEmail.Length > User.EmailMaxLength)
            {
                problem = UserErrors.UploadEmail;
            }
            else if (role is null)
            {
                problem = UserErrors.UploadRole;
            }
            else if (!seenIds.Add(hash) || await users.LookupHashExistsAsync(hash, cancellationToken))
            {
                problem = UserErrors.NationalIdTaken;
            }
            else if (!seenEmails.Add(normalEmail) || await users.EmailExistsAsync(normalEmail, cancellationToken))
            {
                problem = UserErrors.EmailTaken;
            }

            results.Add(new UploadRow(line, masked, nameAr, nameEn, email, role?.ToString(), problem?.Code));

            if (problem is null && request.Save)
            {
                var password = RandomNumberGenerator.GetString(PasswordAlphabet, PasswordLength);
                var user = User.Register(protector.Protect(nationalId.Value), name.Value, normalEmail, passwords.Hash(password), role!.Value, Language.Ar, now);
                user.RequirePasswordChange();
                users.Add(user);
                created.Add(new UploadedUser(line, normalEmail, password));
            }
        }

        if (request.Save)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new UserUploadResponse(results.Count(r => r.ErrorCode is null), results.Count(r => r.ErrorCode is not null), results, created);
    }

    /// <summary>Administrators are never created by file. That stays a deliberate, single change.</summary>
    private static Role? ReadRole(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Role.Trainee;
        }

        return Enum.TryParse<Role>(text, true, out var role) && Enum.IsDefined(role) && role != Role.Admin ? role : null;
    }
}
