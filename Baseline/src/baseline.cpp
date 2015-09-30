#include <iostream>
#include <fstream>
#include <string>
#include <cstdlib>
#include <time.h>
#include <sys/time.h>
#include <vector>
#include <string>
#include <algorithm>
#include <cmath>
#include "rapidjson/filereadstream.h"
#include "fm.h"

using namespace std;
using namespace rapidjson;

vector<string> split(const string s, const string delim)
{
    vector<string> result;
    size_t start = 0U;
    size_t end = s.find(delim);
    while (end != string::npos)
    {
        result.push_back(s.substr(start, end - start));
        start = end + delim.length();
        end = s.find(delim, start);
    }
    result.push_back(s.substr(start, end));
    return result;
}

int user_count = 0;
int place_count = 0;

void load_data(const char* file, FMFeature& feat, bool skip_zero)
{
    ifstream fin(file);
    string s;
    getline(fin, s);
    vector<string> v = split(s, ",");
    place_count = v.size() - 2;
    int user = v.size() - 1;
    while(getline(fin, s), fin)
    {
        v = split(s, ",");
        for (int i = 2; i < v.size(); i++)
        {
            int cnt = atoi(v[i].c_str());
            if (true)
            {
                feat.target.push_back(cnt);
                std::vector<std::pair<int, double> > f;
                f.push_back(make_pair(i - 1, 1.0));
                f.push_back(make_pair(user, 1.0));
                feat.feature.push_back(f);
            }
        }
        user++;
    }
    user_count = user - place_count;
}

void run(char* configFilename)
{
    Document config;
    FILE* fp = fopen(configFilename, "r");
    char buffer[4096];
    FileReadStream configFile(fp, buffer, sizeof(buffer));
    config.ParseStream(configFile);
    
    FMFeature trainData, testData;
    FMTarget prediction;

    load_data(config["train_data"].GetString(), trainData, true);
    load_data(config["test_data"].GetString(), testData, false);
    
    fm_train_test(config, trainData, testData, prediction);

    ofstream fout(config["pred"].GetString());
    for (int i = 0; i < prediction.size(); i++)
    {
        fout << prediction[i] << ' ';
        int place = testData.feature[i][0].first;
        if (place == place_count)
            fout << endl;
    }
}

int main(int argc, char* argv[])
{
    struct timeval tv;
    gettimeofday(&tv, 0);
    srand(1);

    if (argc < 2)
    {
        cout << "Please specify a config file." << endl;
        return 0;
    }

    run(argv[1]);

    return 0;
}


