namespace CurseWork
{
    public class MainViewModel
    {
        public double MSE { get; set; }
        public double RMSE { get; set; }
        public double R2 { get; set; }
        public double AdjustedR2 { get; set; }

        public MainViewModel()
        {
            MSE = 0;
            RMSE = 0;
            R2 = 0;
            AdjustedR2 = 0;
        }
    }
}