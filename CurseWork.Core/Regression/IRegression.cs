namespace CurseWork.Core.Regression;

/// <summary>
/// Общий интерфейс для любого метода регрессии.
/// </summary>
/// <typeparam name="TResult">Тип результата (содержит коэффициенты, предсказания, метрики).</typeparam>
public interface IRegression<TResult>
{
    TResult Fit();
}