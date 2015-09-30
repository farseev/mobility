#include <iterator>
#include <algorithm>
#include <iomanip>
#include "fm.h"
#include "libfm/util/util.h"
#include "libfm/fm_core/fm_model.h"
#include "libfm/libfm/Data.h"
#include "libfm/libfm/fm_learn.h"
#include "libfm/libfm/fm_learn_sgd.h"
#include "libfm/libfm/fm_learn_sgd_element.h"
#include "libfm/libfm/fm_learn_sgd_element_adapt_reg.h"
#include "libfm/libfm/fm_learn_mcmc_simultaneous.h"
#include "rapidjson/document.h"

using namespace std;
using namespace rapidjson;

struct FMMemory
{
	FMMemory() : data(0), cache(0), data_t(0), cache_t(0)
	{
	}
	~FMMemory()
	{
		if (data)
			delete data;
		if (cache)
			delete[] cache;
		if (data_t)
			delete data_t;
		if (cache_t)
			delete[] cache_t;
	}
	LargeSparseMatrixMemory<DATA_FLOAT>* data;
	sparse_entry<DATA_FLOAT>* cache;
	LargeSparseMatrixMemory<DATA_FLOAT>* data_t;
	sparse_entry<DATA_FLOAT>* cache_t;
};

void CreateDataT(Data& fmData, FMMemory& mem) {
	// for creating transpose data, the data has to be memory-data because we use random access
	DVector< sparse_row<DATA_FLOAT> >& data = ((LargeSparseMatrixMemory<DATA_FLOAT>*)(fmData.data))->data;

	fmData.data_t = new LargeSparseMatrixMemory<DATA_FLOAT>();
	mem.data_t = (LargeSparseMatrixMemory<DATA_FLOAT>*)(fmData.data_t);

	DVector< sparse_row<DATA_FLOAT> >& data_t = ((LargeSparseMatrixMemory<DATA_FLOAT>*)(fmData.data_t))->data;

	// make transpose copy of training data
	data_t.setSize(fmData.num_feature);

	// find dimensionality of matrix
	DVector<uint> num_values_per_column;
	num_values_per_column.setSize(fmData.num_feature);
	num_values_per_column.init(0);
	long long num_values = 0;
	for (uint i = 0; i < data.dim; i++) {
		for (uint j = 0; j < data(i).size; j++) {
			num_values_per_column(data(i).data[j].id)++;
			num_values++;
		}
	}

	((LargeSparseMatrixMemory<DATA_FLOAT>*)(fmData.data_t))->num_cols = data.dim;
	((LargeSparseMatrixMemory<DATA_FLOAT>*)(fmData.data_t))->num_values = num_values;

	// create data structure for values
	sparse_entry<DATA_FLOAT>* cache = new sparse_entry<DATA_FLOAT>[num_values];
	mem.cache_t = cache;

	long long cache_id = 0;
	for (uint i = 0; i < data_t.dim; i++) {
		data_t.value[i].data = &(cache[cache_id]);
		data_t(i).size = num_values_per_column(i);
		cache_id += num_values_per_column(i);
	}
	// write the data into the transpose matrix
	num_values_per_column.init(0); // num_values per column now contains the pointer on the first empty field
	for (uint i = 0; i < data.dim; i++) {
		for (uint j = 0; j < data(i).size; j++) {
			uint f_id = data(i).data[j].id;
			uint cntr = num_values_per_column(f_id);
			assert(cntr < (uint) data_t(f_id).size);
			data_t(f_id).data[cntr].id = i;
			data_t(f_id).data[cntr].value = data(i).data[j].value;
			num_values_per_column(f_id)++;
		}
	}
	num_values_per_column.setSize(0);
}

void CreateData(Data& fmData, FMFeature rawData, FMMemory& mem)
{
	fmData.data = new LargeSparseMatrixMemory<DATA_FLOAT>();
	mem.data = (LargeSparseMatrixMemory<DATA_FLOAT>*)(fmData.data);

	DVector<sparse_row<DATA_FLOAT> >& data = ((LargeSparseMatrixMemory<DATA_FLOAT>*)(fmData.data))->data;

	int num_rows = 0;
	uint64 num_values = 0;
	fmData.num_feature = 0;
	fmData.min_target = +numeric_limits<DATA_FLOAT>::max();
	fmData.max_target = -numeric_limits<DATA_FLOAT>::max();

	// (1) determine the number of rows and the maximum feature_id
	{
		num_rows = rawData.target.size();
		for (int i = 0; i < rawData.target.size(); i++)
		{
			fmData.min_target = min((float)rawData.target[i], fmData.min_target);
			fmData.max_target = max((float)rawData.target[i], fmData.max_target);
		}
		for (int i = 0; i < rawData.feature.size(); i++)
		{
			num_values += rawData.feature[i].size();
			for (int j = 0; j < rawData.feature[i].size(); j++)
				fmData.num_feature = max(rawData.feature[i][j].first, fmData.num_feature);
		}
	}

	fmData.num_feature++;
	cout << "num_rows=" << num_rows << "\tnum_values=" << num_values
			<< "\tnum_features=" << fmData.num_feature << "\tmin_target="
			<< fmData.min_target << "\tmax_target=" << fmData.max_target
			<< endl;
	data.setSize(num_rows);
	fmData.target.setSize(num_rows);

	((LargeSparseMatrixMemory<DATA_FLOAT>*) (fmData.data))->num_cols = fmData.num_feature;
	((LargeSparseMatrixMemory<DATA_FLOAT>*) (fmData.data))->num_values = num_values;

	sparse_entry<DATA_FLOAT>* cache = new sparse_entry<DATA_FLOAT> [num_values];
	mem.cache = cache;

	// (2) read the data
	{
		int row_id = 0;
		uint64 cache_id = 0;
		while (row_id != rawData.target.size())
		{
			fmData.target.value[row_id] = rawData.target[row_id];
			data.value[row_id].data = &(cache[cache_id]);
			data.value[row_id].size = rawData.feature[row_id].size();

			for (int i = 0; i < rawData.feature[row_id].size(); i++)
			{
				cache[cache_id].id = rawData.feature[row_id][i].first;
				cache[cache_id].value = rawData.feature[row_id][i].second;
				cache_id++;
			}
			row_id++;
		}
		assert(num_rows == row_id);
		assert(num_values == cache_id);
	}

	fmData.num_cases = fmData.target.dim;
}


int fm_train_test(Value& config, FMFeature trainData, FMFeature testData, FMTarget& prediction)
{
	try
	{
		// (1) Load the data
		std::cout << "Loading train...\t" << std::endl;

		bool has_x = (string(config["method"].GetString()) != "mcmc"); // no original data for mcmc
		bool has_xt = (string(config["method"].GetString()) != "sgd" // no transpose data for sgd, sgda
				&& string(config["method"].GetString()) != "sgda");

		Data train(0, has_x, has_xt);

		FMMemory trainMem, testMem;

		CreateData(train, trainData, trainMem);
		if (has_xt)
			CreateDataT(train, trainMem);

		std::cout << "Loading test... \t" << std::endl;
		Data test(0, has_x, has_xt); // no transpose data for sgd, sgda

		CreateData(test, testData, testMem);
		if (has_xt)
			CreateDataT(test, testMem);

		uint num_all_attribute = train.num_feature;

		DataMetaInfo meta(num_all_attribute);
		//meta.num_attr_per_group.setSize(meta.num_attr_groups);
		//meta.num_attr_per_group.init(0);
		meta.num_relations = train.relation.dim;

		// (2) Setup the factorization machine
		fm_model fm;
		{
			fm.num_attribute = num_all_attribute;
			fm.init_stdev = config["init_stdev"].GetDouble();
			// set the number of dimensions in the factorization
			{
				const Value& dimValue = config["dim"];
				vector<int> dim;
				for (int i = 0; i < dimValue.Size(); i++)
					dim.push_back(dimValue[i].GetInt());
				assert(dim.size() == 3);
				fm.k0 = dim[0] != 0;
				fm.k1 = dim[1] != 0;
				fm.num_factor = dim[2];
			}
			fm.init();

		}

		// (3) Setup the learning method:
		fm_learn* fml;
		if (string(config["method"].GetString()) == "sgd")
		{
			fml = new fm_learn_sgd_element();
			((fm_learn_sgd*) fml)->num_iter = config["iter"].GetInt();

		}
		else if (string(config["method"].GetString()) == "mcmc")
		{
			fm.w.init_normal(fm.init_mean, fm.init_stdev);
			fml = new fm_learn_mcmc_simultaneous();
			fml->validation = NULL;
			((fm_learn_mcmc*) fml)->num_iter = config["iter"].GetInt();
			((fm_learn_mcmc*) fml)->num_eval_cases = test.num_cases;
			((fm_learn_mcmc*) fml)->do_sample = true;
			((fm_learn_mcmc*) fml)->do_multilevel = true;
		}
		else
			throw "unknown method";

		fml->fm = &fm;
		fml->max_target = train.max_target;
		fml->min_target = train.min_target;
		fml->meta = &meta;

		if (string(config["task"].GetString()) == "regression")
		{
			fml->task = 0;
		}
		else if (string(config["task"].GetString()) == "classification")
		{
			fml->task = 1;
			for (uint i = 0; i < train.target.dim; i++)
			{
				if (train.target(i) <= 0.0)
					train.target(i) = -1.0;
				else
					train.target(i) = 1.0;
			}
			for (uint i = 0; i < test.target.dim; i++)
			{
				if (test.target(i) <= 0.0)
					test.target(i) = -1.0;
				else
					test.target(i) = 1.0;
			}
		}
		else
			throw "unknown task";
	 	
		fml->log = NULL;
		fml->init();

		if (string(config["method"].GetString()) == "mcmc")
		{
			// set the regularization; for als and mcmc this can be individual per group
			{
				const Value& regValue = config["regular"];
				vector<double> reg;
				for (int i = 0; i < regValue.Size(); i++)
					reg.push_back(regValue[i].GetDouble());
				assert(
						(reg.size() == 0) || (reg.size() == 1)
								|| (reg.size() == 3)
								|| (reg.size() == (1 + meta.num_attr_groups * 2)));
				if (reg.size() == 0)
				{
					fm.reg0 = 0.0;
					fm.regw = 0.0;
					fm.regv = 0.0;
					((fm_learn_mcmc*) fml)->w_lambda.init(fm.regw);
					((fm_learn_mcmc*) fml)->v_lambda.init(fm.regv);
				}
				else if (reg.size() == 1)
				{
					fm.reg0 = reg[0];
					fm.regw = reg[0];
					fm.regv = reg[0];
					((fm_learn_mcmc*) fml)->w_lambda.init(fm.regw);
					((fm_learn_mcmc*) fml)->v_lambda.init(fm.regv);
				}
				else if (reg.size() == 3)
				{
					fm.reg0 = reg[0];
					fm.regw = reg[1];
					fm.regv = reg[2];
					((fm_learn_mcmc*) fml)->w_lambda.init(fm.regw);
					((fm_learn_mcmc*) fml)->v_lambda.init(fm.regv);
				}
				else
				{
					fm.reg0 = reg[0];
					fm.regw = 0.0;
					fm.regv = 0.0;
					int j = 1;
					for (uint g = 0; g < meta.num_attr_groups; g++)
					{
						((fm_learn_mcmc*) fml)->w_lambda(g) = reg[j];
						j++;
					}
					for (uint g = 0; g < meta.num_attr_groups; g++)
					{
						for (int f = 0; f < fm.num_factor; f++)
						{
							((fm_learn_mcmc*) fml)->v_lambda(g, f) = reg[j];
						}
						j++;
					}
				}

			}
		}
		else
		{
			// set the regularization; for standard SGD, groups are not supported
			{
				const Value& regValue = config["regular"];
				vector<double> reg;
				for (int i = 0; i < regValue.Size(); i++)
					reg.push_back(regValue[i].GetDouble());
				assert(
						(reg.size() == 0) || (reg.size() == 1)
								|| (reg.size() == 3));
				if (reg.size() == 0)
				{
					fm.reg0 = 0.0;
					fm.regw = 0.0;
					fm.regv = 0.0;
				}
				else if (reg.size() == 1)
				{
					fm.reg0 = reg[0];
					fm.regw = reg[0];
					fm.regv = reg[0];
				}
				else
				{
					fm.reg0 = reg[0];
					fm.regw = reg[1];
					fm.regv = reg[2];
				}
			}
		}
		{
			fm_learn_sgd* fmlsgd = dynamic_cast<fm_learn_sgd*>(fml);
			if (fmlsgd)
			{
				// set the learning rates (individual per layer)
				{
					const Value& lrValue = config["learn_rate"];
					vector<double> lr;
					for (int i = 0; i < lrValue.Size(); i++)
						lr.push_back(lrValue[i].GetDouble());
					assert((lr.size() == 1) || (lr.size() == 3));
					if (lr.size() == 1)
					{
						fmlsgd->learn_rate = lr[0];
						fmlsgd->learn_rates.init(lr[0]);
					}
					else
					{
						fmlsgd->learn_rate = 0;
						fmlsgd->learn_rates(0) = lr[0];
						fmlsgd->learn_rates(1) = lr[1];
						fmlsgd->learn_rates(2) = lr[2];
					}
				}
			}
		}

		// () learn
		fml->learn(train, test);

		// () Prediction at the end  (not for mcmc and als)
		if (string(config["method"].GetString()) != "mcmc")
		{
			std::cout << "Final\t" << "Train=" << fml->evaluate(train)
					<< "\tTest=" << fml->evaluate(test) << std::endl;
		}

		// () Save prediction
		DVector<double> pred;
		pred.setSize(test.num_cases);
		fml->predict(test, pred);
		for (int i = 0; i < test.num_cases; i++)
			prediction.push_back(pred(i));
		if (config["pred_output"].GetBool())
			pred.save(config["pred"].GetString());

		if (string(config["method"].GetString()) == "sgd")
		{
			fm_learn_sgd_element* fml_sgd = dynamic_cast<fm_learn_sgd_element*>(fml);
			delete fml_sgd;

		}
		else if (string(config["method"].GetString()) == "mcmc")
		{
			fm_learn_mcmc_simultaneous* fml_mcmc = dynamic_cast<fm_learn_mcmc_simultaneous*>(fml);
			delete fml_mcmc;
		}

	} catch (std::string &e)
	{
		std::cerr << std::endl << "ERROR: " << e << std::endl;
	} catch (char const* &e)
	{
		std::cerr << std::endl << "ERROR: " << e << std::endl;
	}

	return 0;
}
