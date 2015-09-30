#ifndef __FM_H
#define __FM_H
#include <vector>
#include "rapidjson/document.h"

typedef std::vector<double> FMTarget;

struct FMFeature
{
	FMTarget target;
	std::vector<std::vector<std::pair<int, double> > > feature;
};

int fm_train_test(rapidjson::Value& config, FMFeature trainData, FMFeature testData, FMTarget& prediction);

#endif