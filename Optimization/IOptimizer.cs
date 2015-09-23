namespace Optimization
{
	public interface IOptimizer<T, D>
	{
		IOptimizerListener<T, D> OptimiserListener { get; set; }

		void Iterate(double functionValue, double[] parameters);

		OptimizationMode Mode { get; set; }

		T OptimizationStep { get; set; }

		T OptimizationLowerBound { get; set; }

		T OptimizationUpperBound { get; set; }
	}
}
