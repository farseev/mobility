using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using C5;
using FeatureExtractor.Properties;
using FNLPTools;
using FNLPTools.Filters;
using FNLPTools.Structs;
using FNLPTools.Tools;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace FeatureExtractor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MongoDatabase database;
        private MongoCollection<BsonDocument> checkinsTable;
        private MongoCollection<BsonDocument> usersTable;
        private MongoCollection<BsonDocument> tweetsTable;
        private MongoCollection<BsonDocument> mediaTable;
        private MongoCollection<BsonDocument> endomondoWorkouts;
		private MongoCollection<BsonDocument> endomondoProfiles;
		private MongoCollection<BsonDocument> foursquareVenueCategories;
        private const string tweetsPathFileName = "imagesFilesList";

        public MainWindow()
        {
            InitializeComponent();

            var arguments = Environment.GetCommandLineArgs().ToList();

            database =
               new MongoClient(arguments[arguments.IndexOf("-db") + 1]).GetServer()
                   .GetDatabase(arguments[arguments.IndexOf("-db") + 2]);
            usersTable = database.GetCollection<BsonDocument>(arguments[arguments.IndexOf("-db") + 3]);
            tweetsTable = database.GetCollection<BsonDocument>(arguments[arguments.IndexOf("-db") + 4]);
            checkinsTable = database.GetCollection<BsonDocument>(arguments[arguments.IndexOf("-db") + 5]);
            mediaTable = database.GetCollection<BsonDocument>(arguments[arguments.IndexOf("-db") + 6]);
			endomondoProfiles = database.GetCollection<BsonDocument>(arguments[arguments.IndexOf("-db") + 7]);
            endomondoWorkouts = database.GetCollection<BsonDocument>(arguments[arguments.IndexOf("-db") + 8]);
            foursquareVenueCategories = database.GetCollection<BsonDocument>(arguments[arguments.IndexOf("-db") + 9]);
            //foursquareUserDetails = database.GetCollection<BsonDocument>(arguments[arguments.IndexOf("-db") + 9]);

        }

		private void GetCategoriesListRecursive(IEnumerable<BsonValue> array, SortedList<string, string> result)
		{
			foreach (var element in array)
			{
				var categories = element["categories"].AsBsonArray;
				var id = element["id"].AsString;
				var name = element["name"].AsString;

				if (!result.ContainsKey(id))
				{
					result.Add(id, name);
				}

				if (categories.Count != 0)
				{
					GetCategoriesListRecursive(categories, result);
				}
			}
		}

		private SortedList<string, string> GetFoursquareVenueCategories()
	    {
		    var venueCategories = foursquareVenueCategories.FindOne();

			var result = new SortedList<string, string>();

			GetCategoriesListRecursive(venueCategories["response"]["categories"].AsBsonArray, result);

		    return result;
	    }

        private void generateTwitterFeaturesForEachMessage()
        {
            var textFeaturesOutputTableName = TextBoxTextFeatures.Text;
            var textFeaturesOutputTable = database.GetCollection<BsonDocument>(textFeaturesOutputTableName);

            var sentimentAnalyser = new SentimentAnalyser();

            var spellChecker = new SpellCheckCorrector(new TextBox());

            Task.Run(() =>
            {
                var filterer = new Filterer();
                filterer.Filters.Add("HashTags", new HashTagsFilter());
                filterer.Filters.Add("Slang", new SlangCorrector());
                filterer.Filters.Add("Url", new UrlFilter());
                filterer.Filters.Add("UserMentionsAndPlaceMentions", new UserMentionsAndPlaceMentionsFilter());
                filterer.Filters.Add("RepeatedChars", new RepeatedCharsFilter());

                Write("Requesting all user's tweets...");


                try
                {
                    var logMessageCount = 0;
                    //var tweets = tweetsTable.FindAll();

                    //Write("Total number of tweets:" + tweets.Count());

                    foreach (var tweet in tweetsTable.Find(Query.EQ("isRetweet", false)))
                    {
                       // if (tweet.GetElement("isRetweet").Value.AsBoolean) continue; //Tweet is retweeted

                        if (textFeaturesOutputTable.FindOne(Query.EQ("_id", tweet["_id"])) != null) continue;

                        var text = tweet.GetValue("text").AsString;


                        int totalFiltered;
                        text = filterer.Filter(text, out totalFiltered);

                        int filtered;
                        spellChecker.Filter(text, out filtered);
                        var errorNumber = spellChecker.LastNumberOfErrors; //IMPORTANT FEATURE FOR AGE PREDICTION
                        var numberOfTerms = text.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries).Count();

                        bool rejected = false;

                        if (errorNumber * 2 > numberOfTerms)
                        {
                            logMessageCount++;
                            if (logMessageCount % 1000 == 0)
                            {
                                rejected = true;
                                Write(logMessageCount + ": Tweet : " + text + " considered as not english and skipped");
                            }
                        }

                        int sentiwords;
                        int emoticons;
                        var sentiscore = sentimentAnalyser.GetSentiments(text, out sentiwords, out emoticons);
                    }

                    Write("Finished");
                }
                catch (Exception exc)
                {
                    Write(exc.ToString());
                }
            });
        }

        private void generateTwitterUserFeatures()
        {
            var testSetSeparationDate = Calendar.SelectedDate;

            var writeFeaturesToDB = CheckBoxExtractTextFeatures.IsChecked.Value;
            var textFeaturesOutputTableName = TextBoxTextFeatures.Text;
            var textFeaturesOutputTable = database.GetCollection<BsonDocument>(textFeaturesOutputTableName);
            var useUsersTable = !CheckBoxDoNotUseUsersTable.IsChecked.Value;

            var sentimentAnalyser = new SentimentAnalyser();

            var spellChecker = new SpellCheckCorrector(new TextBox());
            var outputFolder = TextBoxUserTweetsOutputPath.Text;

            Task.Run(() =>
            {
                var filterer = new Filterer();
                filterer.Filters.Add("HashTags", new HashTagsFilter());
                filterer.Filters.Add("Slang", new SlangCorrector());
                filterer.Filters.Add("Url", new UrlFilter());
                filterer.Filters.Add("UserMentionsAndPlaceMentions", new UserMentionsAndPlaceMentionsFilter());
                filterer.Filters.Add("RepeatedChars", new RepeatedCharsFilter());

                Write("Creating index on twitter_name field");
                tweetsTable.CreateIndex(new IndexKeysBuilder().Ascending("data.Creator._id"));
                //tweetsTable.CreateIndex(new IndexKeysBuilder().Ascending("data.user.id"));TODO: check why it is user but not creator ;)
                Write("Index created");

                var users = new List<string>();

                if (useUsersTable)
                {
                    Write("Scanning users table");

                    users.AddRange(usersTable.FindAll().ToList().ConvertAll(x => x.GetValue("_id").AsString)); //.Reverse();//.ToArray();
                }
                else
                {
                    Write("Requesting all distinct users in database...");

                    users.AddRange(tweetsTable.Distinct("data.Creator._id").ToList().ConvertAll(x =>
                    //users.AddRange(tweetsTable.Distinct("data.user.id").ToList().ConvertAll(x =>
                    {
                        try
                        {

                            return x.AsInt64.ToString();
                        }
                        catch (Exception)
                        {
                            return x.AsInt32.ToString();
                        }
                    }));
                }

                Write("Twitter users list obtained: " + users.Count + " users");

                foreach (var userId in users)
                {
                    Write("User: " + userId);

                    var filePath = Path.Combine(outputFolder, userId + ".txt");

                    if (File.Exists(filePath)) continue;

                    #region Features extraction

                    var numberOfHashtags = 0.0;
                    var numberOfSlang = 0.0;
                    var numberOfUrls = 0.0;
                    var numberOfMentions = 0.0;
                    var numberOfRepeatedChars = 0.0;
                    var numberOfEmotionWords = 0.0;
                    var averageSentiLevel = 0.0;
                    var averageSentiScore = 0.0;
                    var numberOfEmoticons = 0.0;
                    var numberOfMisspellings = 0.0;
                    var numberOfMistakes = 0.0;
                    var numberOfRejectedTweets = 0.0;
                    long numberOfTweets = 0;
                    long numberOfTermsTotal = 0;

                    #endregion

                    Write("Requesting all user's tweets...");

                    IEnumerable<BsonDocument> tweets;


                    try
                    {
                        tweets = tweetsTable.Find(Query.EQ("data.Creator._id", long.Parse(userId))).ToList();
                    }
                    catch (OutOfMemoryException)
                    {
                        Write("Can't download all user's tweets due to the memory limit. Processing sequentially...");
                        tweets = tweetsTable.Find(Query.EQ("data.Creator._id", long.Parse(userId)));
                    }

                    Write("Tweets obtained: " + tweets.Count() + " tweets");

                    var tweetTexts = new ArrayList<string>();

                    foreach (var tweet in tweets)
                    {
                        if (tweet.GetElement("isRetweet").Value.AsBoolean) continue; //Tweet is retweeted

                        var text = tweet.GetValue("text").AsString;

                        int totalFiltered;
                        text = filterer.Filter(text, out totalFiltered);

                        int filtered;
                        var spellCheckedTweet = spellChecker.Filter(text, out filtered);
                        var errorNumber = spellChecker.LastNumberOfErrors; //IMPORTANT FEATURE FOR AGE PREDICTION
                        var numberOfTerms = text.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries).Count();
                        if (errorNumber * 2 > numberOfTerms)
                        {
                            //Write("Tweet : " + text + " considered as not english and skipped");
                            //continue; //Tweet is not english or too bad grammar
                        }
                        else
                        {
                            text = spellCheckedTweet;

                            tweetTexts.Add(text);
                        }

                        #region Features extraction

                        numberOfHashtags += filterer.LastResults["HashTags"];
                        numberOfSlang += filterer.LastResults["Slang"];
                        numberOfUrls += filterer.LastResults["Url"];
                        numberOfMentions += filterer.LastResults["UserMentionsAndPlaceMentions"];
                        numberOfRepeatedChars += filterer.LastResults["RepeatedChars"];

                        int sentiwords;
                        int emoticons;
                        var sentiscore = sentimentAnalyser.GetSentiments(text, out sentiwords, out emoticons);

                        averageSentiLevel += Math.Abs(sentiscore);
                        averageSentiScore += sentiscore;
                        numberOfEmoticons += emoticons;
                        numberOfEmotionWords += sentiwords;

                        numberOfMisspellings += filtered;
                        numberOfMistakes += errorNumber;
                        numberOfRejectedTweets += errorNumber * 2 > numberOfTerms ? 1 : 0;
                        numberOfTermsTotal += numberOfTerms;

                        #endregion

                        numberOfTweets++;
                    }


                    File.WriteAllLines(filePath, tweetTexts);

                    if (writeFeaturesToDB)
                    {
                        if (numberOfTweets == 0 || numberOfTermsTotal == 0) continue;

                        var textFeaturesDoc = new BsonDocument
                            {
                                {"_id", userId},
                                {"numberOfHashtags", numberOfHashtags/numberOfTweets},
                                {"numberOfSlang", numberOfSlang/numberOfTermsTotal},
                                {"numberOfUrls", numberOfUrls/numberOfTweets},
                                {"numberOfMentions", numberOfMentions/numberOfTweets},
                                {"numberOfRepeatedChars", numberOfRepeatedChars/numberOfTermsTotal},
                                {"numberOfEmotionWords", numberOfEmotionWords/numberOfTermsTotal},
                                {"numberOfEmoticons", numberOfEmoticons/numberOfTermsTotal},
                                {"averageSentiLevel", averageSentiLevel/numberOfTweets},
                                {"averageSentiScore", averageSentiScore/numberOfTweets},
                                {"numberOfMisspellings", numberOfMisspellings/numberOfTermsTotal},
                                {"numberOfMistakes", numberOfMistakes/numberOfTermsTotal},
                                {"numberOfRejectedTweets", numberOfRejectedTweets/numberOfTweets},
                                {"numberOfTermsAverage", (double) numberOfTermsTotal/numberOfTweets},
                                {"numberOfTweets", numberOfTweets}
                            };

                        textFeaturesOutputTable.Insert(textFeaturesDoc);
                    }
                }

                Write("Finished");
            });
        }

        private void ButtonUserTweetFiles_Click(object sender, RoutedEventArgs e)
        {
            if (CheckBoxExtractTextFeaturesEachTweet.IsChecked != null &&
                CheckBoxExtractTextFeaturesEachTweet.IsChecked.Value)
            {
                generateTwitterFeaturesForEachMessage();
            }
            else
            {
                generateTwitterUserFeatures();
            }
        }

        private void ButtonCreateTweetsPathFile_Click(object sender, RoutedEventArgs e)
        {
            var imagesFolder = TextBoxImagesFilesFolder.Text;
            var filesInDir = Directory.GetFiles(imagesFolder, "*.jpg");

            var filesToProcess = new List<string>();

            foreach (var file in filesInDir)
            {
                if (File.Exists(Path.Combine(new[]
                {
                    Path.GetDirectoryName(file),
                    Path.GetFileNameWithoutExtension(file) + ".txt"
                }))) continue;

                filesToProcess.Add(file);
            }

            File.Delete(Path.Combine(imagesFolder, tweetsPathFileName + ".txt"));
            File.WriteAllLines(Path.Combine(imagesFolder, tweetsPathFileName + ".txt"), filesToProcess);
        }

        private void ButtonCreateCategoryWordDocuments_Click(object sender, RoutedEventArgs e)
        {
	        var splitDataByDate = CheckBoxSplitDataByDate.IsChecked.Value;
	        var normalize = CheckBoxNormaliseFeatures.IsChecked.Value;
	        var dataSplitTime = Calendar.DisplayDate;
            var categoryFeaturesOutputTableName = TextBoxCategoryFeatures.Text;
			var categoryFeaturesOutputTable = database.GetCollection<BsonDocument>(categoryFeaturesOutputTableName + "Before " + dataSplitTime);
			var categoryFeaturesOutputTableTest = database.GetCollection<BsonDocument>(categoryFeaturesOutputTableName + "After " + dataSplitTime);
            var outputFolder = TextBoxCategoriesFilesFolder.Text;
	        var output = new ArrayList<Tuple<BsonDocument[], string, List<string>>>();

	        dynamic multiplier = 1;

	        if (normalize)
	        {
		        multiplier = 1.0;
	        }

	        var users = usersTable.FindAll().ToArray();
            
	        var categories = GetFoursquareVenueCategories();

            var features = new SortedDictionary<string, int[]>();

            foreach (var category in categories)
            {
                if (features.ContainsKey(category.Key)) continue;

                features.Add(category.Key, new []{0, 0});
            }

            var featuresList = features.ToList();

            File.WriteAllLines(Path.Combine(outputFolder, "CategoriesDescription.csv"), featuresList.ConvertAll(
                x =>
                {
                    foreach (var category in categories)
                    {
                        if (category.Key == x.Key)
                        {
                            return (featuresList.IndexOf(x) + 1)  + 
                                "\t" +
								category.Key + 
								"\t" +
                                category.Value;
                        }
                    }

                    return string.Empty;
                }));

            foreach(var user in users)
            {
                var userId = user["_id"].AsString;

                var filePath = Path.Combine(outputFolder, userId + ".txt");

                if (categoryFeaturesOutputTable.FindOne(Query.EQ("_id", userId)) != null) continue;

                foreach (var key in features.Keys)
	            {
		            features[key][0] = 0;
					features[key][1] = 0;
	            }

                var categoryLDADocument = new List<string>();

                var checkins = checkinsTable.Find(Query.EQ("twitter_id", userId)).ToArray();

                var totalCount = new []{0, 0};

                foreach (var checkin in checkins)
                {
                    try
                    {
                        var venueCategories = checkin["data"]["checkin"]["venue"]["categories"].AsBsonArray;
	                    var createdAt = UnitConversion.UnixTimeStampToDateTime(checkin["data"]["checkin"]["createdAt"].AsInt32);

                        foreach (var venueCategory in venueCategories)
                        {
                            var categoryId = venueCategory["id"].AsString;

							if (dataSplitTime > createdAt || !splitDataByDate)
	                        {
		                        totalCount[0]++;

								features[categoryId][0]++;
	                        }
	                        else
	                        {
								totalCount[1]++;

								features[categoryId][1]++;
	                        }

	                        categoryLDADocument.Add(categoryId);
                        }
                    }
                    catch(Exception exc)
                    {
	                    Write(exc.ToString());
                    }
                }

	            var categoryFeaturesDoc = new[]
	            {
		            new BsonDocument
		            {
			            {"_id", userId},
			            {"categoryMentions", totalCount[0]}
		            },
		            new BsonDocument
		            {
			            {"_id", userId},
			            {"categoryMentions", totalCount[1]}
		            }
	            };

	            var firstSumm = features.Values.ToList().ConvertAll(x => x[0]).Sum();
				var secondSumm = features.Values.ToList().ConvertAll(x => x[1]).Sum();


				if (firstSumm != totalCount[0] || secondSumm != totalCount[1])
	            {
		            Write("Inconsistent features and features count");
	            }

                var counter = 0;

				if (!normalize)//Make denominator equal to 1 if we do not want normalize features
                {
	                totalCount = new[] {1, 1};
                }

				foreach (var userFeature in features)
                {
	                ++counter;
	                categoryFeaturesDoc[0].Add((counter).ToString(CultureInfo.InvariantCulture),
							userFeature.Value[0] / multiplier * totalCount[0]);
					categoryFeaturesDoc[1].Add((counter).ToString(CultureInfo.InvariantCulture),
							userFeature.Value[1] / multiplier * totalCount[1]);
                }

				output.Add(new Tuple<BsonDocument[], string, List<string>>(categoryFeaturesDoc, filePath, categoryLDADocument));
            }

	        foreach (var tuple in output)
	        {
				categoryFeaturesOutputTable.Insert(tuple.Item1[0]);

				if (splitDataByDate)
				{
					categoryFeaturesOutputTableTest.Insert(tuple.Item1[1]);
				}

				File.WriteAllLines(tuple.Item2, tuple.Item3);  
	        }
        }

		private HashDictionary<string, Dictionary<string, List<float>>> usersScores =
			new HashDictionary<string, Dictionary<string, List<float>>>();
        private const int featureSetSize = 1000;
        private const bool binaryFeatures = false;
        private const double treshhold = 0.50;


        private void ButtonConsolidateOutputFiles_Click(object sender, RoutedEventArgs e)
        {
			var writeEachConceptFeature = CheckBoxExtractTextFeaturesEachTweet.IsChecked.Value;
            var concepteaturesOutputTableName = TextBoxConceptFeatures.Text;
            var conceptFeaturesOutputTable = database.GetCollection<BsonDocument>(concepteaturesOutputTableName);
            var imagesFolder = TextBoxImagesFilesFolder.Text;
            var filesInDir = Directory.GetFiles(imagesFolder, "*.txt");

            var normaliseFeatures = CheckBoxNormaliseFeatures.IsChecked.Value;

            Task.Run(() =>
            {
                var filesProcessed = 0;

                foreach (var file in filesInDir)
                {
                    try
                    {
                        var imageId = Path.GetFileNameWithoutExtension(file);
                        
                        if (imageId == tweetsPathFileName) continue;
                        
                        var userRecordValues = File.ReadAllText(file)
                            .Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);

                        var concepts = userRecordValues.Select(userRecordValue => float.Parse(userRecordValue, CultureInfo.InvariantCulture)).ToList();

                        if (featureSetSize != concepts.Count)
                        {
                            Write("Inconsistent features set size: " + concepts.Count + " " + imageId);
                            continue;
                        }

                        var twitterId = mediaTable.FindOne(Query.EQ("_id", imageId))["twitter_id"].AsString;

                        if (!usersScores.Contains(twitterId))
                        {
							usersScores.Add(twitterId, new Dictionary<string, List<float>>());
                        }

                        usersScores[twitterId].Add(imageId, concepts);
                    }
                    catch (Exception exc)
                    {
                        Write(exc.ToString());
                    }

                    filesProcessed++;

                    if (filesProcessed % 1000 == 0)
                    {
                        Write("File processed: " + filesProcessed);
                    }
                }

                var min = double.MaxValue;
                var max = double.MinValue;

                foreach (var userScore in usersScores)
                {
                    foreach (var scoreList in userScore.Value)
                    {
                        foreach (var score in scoreList.Value)
                        {
                            if (score < min) min = score;
                            if (score > max) max = score;
                        }
                    }
                }

                double range;
                double topUp;

                if (Math.Sign(max) == Math.Sign(min))
                {
                    range = Math.Abs(max) - Math.Abs(min);
                    topUp = - Math.Abs(min);
                }
                else
                {
                    range = Math.Abs(max) + Math.Abs(min);
                    topUp = Math.Abs(min);
                }

                Write("Max: " + max);
                Write("Min: " + min);
                Write("Range: " + range);
                Write("Topup: " + topUp);

	            foreach (var userScore in usersScores)
	            {
		            if (writeEachConceptFeature)
		            {
			            foreach (var scoreList in userScore.Value)
			            {
							var conceptFeaturesDoc = new BsonDocument
							{
								{"_id", scoreList.Key},
								{"concepts", new BsonArray(scoreList.Value)}
							};

				            conceptFeaturesOutputTable.Insert(conceptFeaturesDoc);
			            }
		            }
		            else
		            {
			            if (conceptFeaturesOutputTable.FindOne(Query.EQ("_id", userScore.Key)) != null) continue;

			            var probabilities = new Dictionary<int, double>();
			            var numberOfPicturesTaken = userScore.Value.Count;

			            var conceptFeaturesDoc = new BsonDocument
			            {
				            {"_id", userScore.Key},
				            {"numberOfImages", numberOfPicturesTaken}
			            };

			            foreach (var scoreList in userScore.Value)
			            {
				            var counter = 0;

				            foreach (var score in scoreList.Value)
				            {
					            counter++;
					            var probability = (score + topUp)/range;

					            if (!probabilities.ContainsKey(counter))
					            {
						            probabilities.Add(counter, 0);
					            }

					            if (binaryFeatures)
					            {
						            probabilities[counter] += probability > treshhold ? 1 : 0;
					            }
					            else
					            {
						            probabilities[counter] += probability;
					            }
				            }
			            }

                        if (!normaliseFeatures)
		                {
		                    numberOfPicturesTaken = 1;
		                }

			            var record =
				            probabilities.ToDictionary(probability => probability.Key.ToString(CultureInfo.InvariantCulture),
					            probability => probability.Value/numberOfPicturesTaken);

			            conceptFeaturesDoc.AddRange(record);
			            conceptFeaturesOutputTable.Insert(conceptFeaturesDoc);
		            }
	            }
            });
        }

        #region Endomondo

        #region Ground Truth

		private Dictionary<string, SortedList<DateTime, UserObesityProfile>> groundTruth = new Dictionary<string, SortedList<DateTime, UserObesityProfile>>(); 

        private void ComputeEndomondoGroundTruth_Click(object sender, RoutedEventArgs e)
        {
			endomondoProfiles.CreateIndex(new IndexKeysBuilder().Hashed("twitterId"));

	        //foreach (var twitterId in endomondoProfiles.Distinct("twitterId"))
	        {
				var userProfilesRecords = endomondoProfiles.Find(Query.And(
					Query.Exists("data.webProfile"),
					Query.Exists("data.webProfile.Weight"),
					Query.Exists("data.webProfile.Height"))

				foreach (var userProfileRecord in userProfilesRecords)
		        {
					var id = userProfileRecord["twitterId"].AsString;

					if (!groundTruth.ContainsKey(id))
					{
						groundTruth.Add(id, new SortedList<DateTime,UserObesityProfile>());
					}

			        try
			        {

				        var age = -1;
				        try
				        {
							age = (DateTime.Now - DateTime.Parse(userProfileRecord["data"]["webProfile"]["Birthday"].AsString)).Days / 365;
				        }
				        catch (Exception exc)
				        {
					        
				        }

				        var gender = string.Empty;
				        try
				        {
							gender = userProfileRecord["data"]["webProfile"]["Sex"].AsString;
				        }
				        catch (Exception exc)
				        {
					        
				        }

				        var height = UnitConversion.ConvertHeightPure(userProfileRecord["data"]["webProfile"]["Height"].AsString);
				        var weight = UnitConversion.ConvertWeightPure(userProfileRecord["data"]["webProfile"]["Weight"].AsString);

				        var timeStamp = userProfileRecord["timeStampUTC"].ToUniversalTime();

						groundTruth[id].Add(timeStamp, new UserObesityProfile(weight, height, age, gender));
			        }
			        catch (Exception exc)
			        {
				        
			        }
		        }
	        }

            
            var obesityGroundTruthOutputTableName = ObesityGroundTruthTable.Text;
            var obesityGroundTruthOutputTable = database.GetCollection<BsonDocument>(obesityGroundTruthOutputTableName);

			foreach (var userId in groundTruth.Keys)
			{
				var userGroundTruth = groundTruth[userId];

				var averageTrend = 0.0;

				for (var i = 1; i < userGroundTruth.Count; i++)
				{
					userGroundTruth.ElementAt(i).Value.Trend = userGroundTruth.ElementAt(i).Value.BMI - userGroundTruth.ElementAt(i - 1).Value.BMI;

					averageTrend += userGroundTruth.ElementAt(i).Value.Trend;
				}

				if (userGroundTruth.Count == 0) continue;

				var lastRecord = userGroundTruth.ElementAt(userGroundTruth.Count - 1).Value;

				try
				{
					obesityGroundTruthOutputTable.Insert(new BsonDocument
					{
						{"_id", userId},
						{"Gender", lastRecord.Gender},
						{"Age", lastRecord.Age},
						{"averageTrend", averageTrend},
						{"LastClass", lastRecord.ObesityClass},
						{"LastBMI", lastRecord.BMI},
						//{"HasHRData", sensor},
						{
							"groundTruth",
							new BsonArray(userGroundTruth.ToList().ConvertAll(x => new BsonDocument
							{
								{"Height", x.Value.Height},
								{"Weight", x.Value.Weight},
								{"BMI", x.Value.BMI},
								{"Trend", x.Value.Trend},
								{"Class", x.Value.ObesityClass}
							}))
						}
					});
				}
				catch { }
			}
        }

        #endregion

        #endregion

        #region Log

        public void Write(string message)
        {
            try
            {
                if (RichTextBoxLog.Dispatcher.CheckAccess())
                {
                    RichTextBoxLog.AppendText(message + "\n");
                    RichTextBoxLog.ScrollToEnd();
                }
                else
                {
                    RichTextBoxLog.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal,
                        new Action<string>(Write), message);
                }
            }
            catch (Exception exc)
            {
            }
        }

        #endregion

        #region Properties save

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Settings.Default.Save();
        }

        #endregion
    }
}
