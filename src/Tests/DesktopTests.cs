using System.Globalization;
using FluentAssertions;
using ToirProf.Desktop.Services;

namespace ToirProf.Tests;

// ===========================================================================
//  Ровно пять юнит-тестов для Desktop-клиента по Приложению 5 тематического
//  сценария. Каждый тест — один метод с несколькими случаями внутри, чтобы
//  число тестов оставалось равным пяти, а все ветви функций были покрыты:
//
//  Тест №1 — calculateDowntimeHours: корректные даты.
//  Тест №2 — calculateDowntimeHours: окончание раньше начала, null-границы.
//  Тест №3 — validateWorkOrder: все поля корректны.
//  Тест №4 — validateWorkOrder: нет обязательного поля, аварийная без причины,
//            неверный порядок плановых дат, начало в прошлом.
//  Тест №5 — calculateTotalMaterialCost: эталон 7300,00, пустой список,
//            нулевое количество, отрицательные значения.
//
//  Структура каждого теста — Arrange / Act / Assert. Тесты не обращаются
//  ни к базе, ни к сети, ни к файлам: функции чистые.
// ===========================================================================
public sealed class DesktopTests : IDisposable
{
    private static readonly Guid EquipmentId = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Bearing = new("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid Belt = new("eeeeeeee-0000-0000-0000-000000000002");
    private static readonly Guid Oil = new("eeeeeeee-0000-0000-0000-000000000003");

    /// <summary>Фиксированная «сегодняшняя» дата: результат не зависит от дня запуска.</summary>
    private static readonly DateOnly Today = new(2026, 3, 20);

    private readonly CultureInfo _savedCulture;

    // setUp: xUnit создаёт новый экземпляр класса на каждый тест, поэтому
    // конструктор — это подготовка, а Dispose — сброс состояния после теста.
    // Культуру фиксируем, чтобы числа и даты не зависели от настроек машины.
    public DesktopTests()
    {
        _savedCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    // tearDown
    public void Dispose() => CultureInfo.CurrentCulture = _savedCulture;

    private static WorkOrderDraft ValidDraft() => new(
        EquipmentId,
        "Стучит подшипник главного шпинделя",
        "Критический",
        "Создана",
        new DateOnly(2026, 3, 24),
        new DateOnly(2026, 3, 26),
        "Износ подшипника 6-205 на шпинделе");

    // =======================================================================
    //  Тест №1. calculateDowntimeHours — корректные даты
    // =======================================================================
    [Fact(DisplayName = "Тест 1: простой считается по рабочим часам между корректными датами")]
    public void calculateDowntimeHours_ValidDates_ReturnsCorrectHours()
    {
        // Arrange — 01.08.2026 (суббота) 10:00 → 03.08.2026 (понедельник) 16:00.
        // Выходные не считаются, только понедельник 8:00–16:00 = 8 часов.
        var start = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 8, 3, 16, 0, 0, TimeSpan.Zero);

        // Act
        var hours = Calculators.CalculateDowntimeHours(start, end);

        // Assert
        hours.Should().Be(8m);
    }

    // =======================================================================
    //  Тест №2. calculateDowntimeHours — окончание раньше начала и null
    // =======================================================================
    [Fact(DisplayName = "Тест 2: окончание раньше начала и пустые даты дают ноль")]
    public void calculateDowntimeHours_EndBeforeStartOrNullDates_ReturnsZero()
    {
        // Arrange
        var start = new DateTimeOffset(2026, 3, 20, 20, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 3, 20, 8, 0, 0, TimeSpan.Zero);

        // Act
        var reversed = Calculators.CalculateDowntimeHours(start, end);
        var noStart = Calculators.CalculateDowntimeHours(null, end);
        var noEnd = Calculators.CalculateDowntimeHours(start, null);
        var nothing = Calculators.CalculateDowntimeHours(null, null);

        // Assert — ноль без исключения во всех случаях.
        reversed.Should().Be(0m);
        noStart.Should().Be(0m);
        noEnd.Should().Be(0m);
        nothing.Should().Be(0m);
    }

    // =======================================================================
    //  Тест №3. validateWorkOrder — все поля корректны
    // =======================================================================
    [Fact(DisplayName = "Тест 3: заполненная заявка проходит проверку")]
    public void validateWorkOrder_AllFieldsValid_ReturnsTrue()
    {
        // Arrange
        var draft = ValidDraft();

        // Act
        var result = Calculators.ValidateWorkOrder(draft, Today);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // =======================================================================
    //  Тест №4. validateWorkOrder — нарушения
    // =======================================================================
    [Fact(DisplayName = "Тест 4: пропуски, аварийная без причины и неверные даты возвращают ошибки")]
    public void validateWorkOrder_InvalidFields_ReturnsFalseWithErrors()
    {
        // Arrange
        var noEquipment = ValidDraft() with { EquipmentId = Guid.Empty };
        var noDescription = ValidDraft() with { Description = "   " };
        var emergencyWithoutReason = ValidDraft() with { FailureReason = null };
        var endBeforeStart = ValidDraft() with { PlannedStart = new DateOnly(2026, 3, 26), PlannedEnd = new DateOnly(2026, 3, 24) };
        var startInPast = ValidDraft() with { PlannedStart = new DateOnly(2026, 3, 10), PlannedEnd = new DateOnly(2026, 3, 26) };

        // Act
        var r1 = Calculators.ValidateWorkOrder(noEquipment, Today);
        var r2 = Calculators.ValidateWorkOrder(noDescription, Today);
        var r3 = Calculators.ValidateWorkOrder(emergencyWithoutReason, Today);
        var r4 = Calculators.ValidateWorkOrder(endBeforeStart, Today);
        var r5 = Calculators.ValidateWorkOrder(startInPast, Today);

        // Assert — каждая ошибка распознана и названа своим текстом.
        r1.IsValid.Should().BeFalse();
        r1.Errors.Should().ContainSingle().Which.Should().Contain("оборудование");

        r2.IsValid.Should().BeFalse();
        r2.Errors.Should().ContainSingle().Which.Should().Contain("описание");

        r3.IsValid.Should().BeFalse();
        r3.Errors.Should().ContainSingle().Which.Should().Contain("аварийной");

        r4.IsValid.Should().BeFalse();
        r4.Errors.Should().ContainSingle().Which.Should().Contain("окончание");

        r5.IsValid.Should().BeFalse();
        r5.Errors.Should().ContainSingle().Which.Should().Contain("прошлом");
    }

    // =======================================================================
    //  Тест №5. calculateTotalMaterialCost
    // =======================================================================
    [Fact(DisplayName = "Тест 5: стоимость материалов — эталон 7300, пустой список, ноль и отрицательные")]
    public void calculateTotalMaterialCost_VariousLists_ReturnsExpectedSum()
    {
        // Arrange — эталон из задания: 4 × 850 + 2 × 1200 + 10 × 150 = 7300,00.
        var reference = new[]
        {
            new MaterialLine(Bearing, 4m, 850m),
            new MaterialLine(Belt, 2m, 1200m),
            new MaterialLine(Oil, 10m, 150m),
        };
        var empty = Array.Empty<MaterialLine>();
        var withZero = new[] { new MaterialLine(Bearing, 4m, 850m), new MaterialLine(Oil, 0m, 150m) };
        var negativeQuantity = new[] { new MaterialLine(Bearing, -1m, 850m) };
        var negativePrice = new[] { new MaterialLine(Bearing, 1m, -850m) };

        // Act
        var total = Calculators.CalculateTotalMaterialCost(reference);
        var emptyTotal = Calculators.CalculateTotalMaterialCost(empty);
        var zeroTotal = Calculators.CalculateTotalMaterialCost(withZero);
        var negativeQty = () => Calculators.CalculateTotalMaterialCost(negativeQuantity);
        var negativeCost = () => Calculators.CalculateTotalMaterialCost(negativePrice);

        // Assert
        total.Should().Be(7300.00m);
        emptyTotal.Should().Be(0m);
        zeroTotal.Should().Be(3400.00m);
        negativeQty.Should().Throw<ArgumentException>().WithMessage("*отрицательн*");
        negativeCost.Should().Throw<ArgumentException>().WithMessage("*отрицательн*");
    }
}
