namespace CurseWork.Core.Report
{
    public interface IReportSaveStrategy
    {
        void Save(string filePath, IRegressionResult result);
    }
}