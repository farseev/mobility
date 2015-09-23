using System;
using System.Collections.Generic;

namespace Optimization
{
	public interface IOptimizerListener<T, D>
	{
		void OptimizationFinished(T[] bestParameters, double bestFunkValue);

        void OptimizationIncriment(T[] parameters, Func<IList<D>, IList<D>, int, double> optimizationFunction);

		IOptimizer<T, D> Optimiser { get; set;  }
	}
}
