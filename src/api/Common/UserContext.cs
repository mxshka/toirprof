using System.Security.Claims;
using ToirProf.Api.Middlewares;

namespace ToirProf.Api.Common;

/// <summary>
/// Данные текущего пользователя из JWT: идентификатор, роль и цех.
///
/// Мастер работает только со своим цехом — так требует лист задания
/// (разграничение доступа по ролям). Руководитель и администратор видят
/// все цеха, поэтому для них <see cref="RestrictedWorkshop"/> возвращает null.
/// </summary>
public static class UserContext
{
    public const string WorkshopClaim = "workshop_id";

    public static Guid GetUserId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw ApiException.Forbidden("Токен не содержит идентификатора пользователя.");

    public static bool IsMaster(this ClaimsPrincipal user)
        => user.IsInRole(RoleCodes.Master);

    public static Guid? GetWorkshopId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(WorkshopClaim), out var id) ? id : null;

    /// <summary>
    /// Цех, которым ограничен пользователь: у мастера — его цех,
    /// у остальных ролей — null (ограничения нет).
    /// </summary>
    public static Guid? RestrictedWorkshop(this ClaimsPrincipal user)
        => user.IsMaster() ? user.GetWorkshopId() : null;

    /// <summary>
    /// Запрет мастеру работать с объектом чужого цеха. Ответ 403, а не 404:
    /// объект существует, но прав на него нет.
    /// </summary>
    public static void EnsureWorkshopAccess(this ClaimsPrincipal user, Guid workshopId)
    {
        if (user.RestrictedWorkshop() is { } own && own != workshopId)
        {
            throw ApiException.Forbidden("Мастер работает только с оборудованием и заявками своего цеха.");
        }
    }
}
