﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.IO;
using System.Xml;
using System.Collections;
using System.Globalization;
using System.Net;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

namespace JiraCSV
{
    class Program
    {
        // *****
        // These bits need to be customised!
        // *****
        // This is the URL to your JIRA server.
        static string jiraBaseURL = "http://jira.example.org/";
        // Where are all of the files created by the Python export script?
        static string localBugsDirectory = @"C:\";
        // What is the URL where you've temporarily stored all of the attachments, including the bug XML files? Note the trailing /
        static string attachmentsTempURL = "http://tmp.example.org/";
        // Where is the name mappings file for those users that exist on JIRA but don't have an entry that
        // will match their LP name?
        static string nameMappingFile = @"C:\Mappings.txt";
        // What is the JIRA Project Name?
        static string jiraProjectName = "JIRA Project";
        // What is the JIRA Project Key?
        static string jiraProjectKey = "JIP";
        // ****
        // End of customisation section
        // ****

        static string base64Credentials;
        static Hashtable jiraNameCache = new Hashtable();
        static List<string> unmatchedNames = new List<string>();

        // Try to convert the provided fullname to the underlying JIRA account name.
        //
        // If the username cannot be matched but there is a space in the name, we return that as a valid
        // name because JIRA will automatically create an account for them. For usernames without spaces,
        // JIRA will assume that is an actual account name, so we return null to force the code to insert
        // that into the text as JIRA will substitute the currently logged on user.
        //
        // The code tries to use the in-memory cache first. This is initially optionally populated from a CSV
        // file then, as users are matched against JIRA, they get added to the cache. That reduces the server
        // load. Note, though, that the in-memory cache is never saved out because it would then complicate
        // matters if names ever got changed on the server or if a different mapping for a name was required.
        static string ConvertToUser(string fullname)
        {
            try
            {
                fullname = fullname.Trim();

                // Do we have an empty string? If so, just return null
                if (String.IsNullOrEmpty(fullname))
                    return null;

                // Have we already failed to match this name?
                if (unmatchedNames.FindIndex(f => f == fullname) != -1)
                    return null;

                // Have we already succeeded in finding this name?
                if (jiraNameCache.ContainsKey(fullname))
                    return jiraNameCache[fullname].ToString();

                // Neither, so let's talk to JIRA to see what it can find for us.
                //
                // API is /rest/api/latest/user/search?username&startAt&maxResults&includeActive&includeInactive
                string url = string.Format("{0}{1}{2}", jiraBaseURL, "rest/api/latest/user/search?username=", System.Web.HttpUtility.UrlEncode(fullname));

                HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
                request.ContentType = "application/json";
                request.Method = "GET";
                request.Headers.Add("Authorization", "Basic " + base64Credentials);

                HttpWebResponse response = request.GetResponse() as HttpWebResponse;
                string result = string.Empty;
                if (response != null)
                {
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        result = reader.ReadToEnd();
                    }
                }

                // Try to convert the JSON into class instances
                var ser = new JavaScriptSerializer();
                var people = ser.Deserialize<List<jira_user>>(result);

                // Assume that the best match is first
                if (people.Count > 0)
                {
                    // Add the name to the cache
                    jiraNameCache.Add(fullname, people[0].name);
                    // and return the value
                    return people[0].name;
                }

                // Failed to find
                // Add fullname to the list of non-matches and return null
                if (unmatchedNames.FindIndex(f => f == fullname) == -1)
                {
                    Console.WriteLine("... unmatched {0}", fullname);
                    unmatchedNames.Add(fullname);
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("exception {0}", ex.Message);

                // An exception occurred - probably because we failed to find the name.
                // Add the failure to the list and return null
                if (unmatchedNames.FindIndex(f => f == fullname) == -1)
                {
                    Console.WriteLine("... unmatched {0}", fullname);
                    unmatchedNames.Add(fullname);
                }
                return null;
            }
        }

        static string ReformatDate(string value)
        {
            // JIRA CSV import defaults to a date/time format of yyyyMMddHHmmss
            // The export files have a date/time format of yyyy-MM-dd HH:mm:ss.xxxxxx+00:00
            // So we start by stripping everything off after the full-stop and then munge
            // the string into the right format.
            string[] substrings = Regex.Split(value, @"[- :.]");

            return string.Format("{0}{1}{2}{3}{4}{5}", substrings[0], substrings[1], substrings[2], substrings[3], substrings[4], substrings[5]);
        }

        static void Main(string[] args)
        {
            if (!String.IsNullOrEmpty(nameMappingFile))
            {
                // Read the mapping file into the in-memory cache. We take a very simplistic approach here and
                // assume that it is just a 2-column CSV file.
                using (StreamReader file = new StreamReader(nameMappingFile))
                {
                    string line;

                    while ((line = file.ReadLine()) != null)
                    {
                        string[] split = Regex.Split(line, ",");
                        Console.WriteLine("Mapping {0} to {1}", split[0], split[1]);
                        jiraNameCache.Add(split[0], split[1]);
                    }
                }
            }

            Console.Write("Username: ");
            string username = Console.ReadLine();

            Console.Write("Password: ");
            string password = Console.ReadLine();

            string mergedCredentials = string.Format("{0}:{1}", username, password);
            byte[] byteCredentials = UTF8Encoding.UTF8.GetBytes(mergedCredentials);
            base64Credentials = Convert.ToBase64String(byteCredentials);

            bool includeHeader = true;
            DirectoryInfo dir = new DirectoryInfo(localBugsDirectory);

            //Hashtable existingUserNamesInJira = new Hashtable();
            //existingUserNamesInJira.Add("Kolbe", "kolbe");
            //existingUserNamesInJira.Add("Igor Babaev", "igor");
            //existingUserNamesInJira.Add("Hartmut Holzgraefe", "hholzgra");
            //existingUserNamesInJira.Add("Timour Katchaounov", "timour");
            //existingUserNamesInJira.Add("Stewart Smith", "stewart");
            //existingUserNamesInJira.Add("Elena Stepanova", "elenst");
            //existingUserNamesInJira.Add("Michael Widenius", "monty");
            //existingUserNamesInJira.Add("Eric Bergen", "ebergen");
            //existingUserNamesInJira.Add("Oleksandr \"Sanja\" Byelkin", "sanja");
            //existingUserNamesInJira.Add("Sergey Petrunia", "psergey");
            //existingUserNamesInJira.Add("Vladislav Vaintroub", "wlad");
            //existingUserNamesInJira.Add("wlad", "wlad");
            //existingUserNamesInJira.Add("Sergei", "serg");
            //existingUserNamesInJira.Add("Kristian Nielsen", "knielsen");
            //existingUserNamesInJira.Add("Alexey Botchkov", "holyfoot");
            //existingUserNamesInJira.Add("Mark Callaghan", "mdcallag");
            //existingUserNamesInJira.Add("Simon J Mudd", "sjmudd");
            //existingUserNamesInJira.Add("Axel Schwenke", "axel");
            //existingUserNamesInJira.Add("Daniel Bartholomew", "dbart");
            //existingUserNamesInJira.Add("Colin Charles", "colin");

            XmlDocument doc;
            int messageCount = 1; //reserve 1 for launchpad bug id
            int lpCommentCount = 0;
            int commentCount = 0;
            int attachmentCount = 0; 
            // count the multiple fields
            foreach (FileInfo file in dir.GetFiles())
            {
                if (!file.Extension.ToLower().Equals(".xml"))
                    continue;

                doc = new XmlDocument();
                doc.Load(file.FullName);

                if (doc.GetElementsByTagName("messages").Count > 0 && messageCount < doc.GetElementsByTagName("messages")[0].ChildNodes.Count + 1)
                    messageCount = doc.GetElementsByTagName("messages")[0].ChildNodes.Count + 1;

                if (doc.GetElementsByTagName("comments").Count > 0 && lpCommentCount < doc.GetElementsByTagName("comments")[0].ChildNodes.Count)
                    lpCommentCount = doc.GetElementsByTagName("comments")[0].ChildNodes.Count;

                if (attachmentCount < doc.GetElementsByTagName("attachment").Count)
                    attachmentCount = doc.GetElementsByTagName("attachment").Count;
            }

            commentCount = messageCount + lpCommentCount;

            DataTable table = new DataTable();
            table.Columns.Add("Project name", typeof(string));
            table.Columns.Add("Project key", typeof(string));
            table.Columns.Add("IssueType", typeof(string));
            table.Columns.Add("Resolution", typeof(string));
            table.Columns.Add("title", typeof(string));
            table.Columns.Add("description", typeof(string));

            for (int i = 0; i < commentCount; i++)
                table.Columns.Add("comment" + (i + 1).ToString(), typeof(string));

            table.Columns.Add("dateCreated", typeof(string));
            table.Columns.Add("dateUpdated", typeof(string));
            table.Columns.Add("owner", typeof(string));
            table.Columns.Add("assignee", typeof(string));
            table.Columns.Add("status", typeof(string));
            table.Columns.Add("importance", typeof(string));
            table.Columns.Add("milestone_title", typeof(string));

            for (int i = 0; i < attachmentCount + 1; i++)
                table.Columns.Add("attachment" + (i + 1).ToString(), typeof(string));
            
            DataRow row;
            // hasky:/media/backup/archive/rasmus/
            string attachmentLink = "";
            string attachmentFilename = "";
            string attachmentOwner = "";
            string attachmentTimestamp = "";
            string commentOwner = "";
            string commentTimestamp = "";
            DateTime dtCommentTimestamp;
            string commentSubject = "";
            string commentText = "";
            string message = "";
            string messageTitle = "";
            string messageFile = "";
            string messageTimestamp = "";
            string messageOwner = "";
            bool debug = false;
            int debugCount = 0;
            string launchpadBugId = "";
            int attachmentAmount = 0;
            string bugOwner = "";
            string bugAssignee = "";
            int lastInsertedComment = 0;
            string convertedName = "";

            foreach (FileInfo file in dir.GetFiles())
            {
                if (!file.Extension.ToLower().Equals(".xml"))
                    continue;

                doc = new XmlDocument();
                doc.Load(file.FullName);

                row = table.NewRow();

                // project info
                row["Project name"] = jiraProjectName;
                row["Project key"] = jiraProjectKey;

                launchpadBugId = doc.DocumentElement.Attributes["id"].Value;
                row["comment1"] = "Launchpad bug id: " + launchpadBugId;

                Console.WriteLine("Processing bug {0}", launchpadBugId);

                row["title"] = "LP:" + launchpadBugId + " - " + doc.GetElementsByTagName("title")[0].InnerText;
                row["description"] = doc.GetElementsByTagName("description")[0].InnerText;

                string compare = "";

                if (row["description"].ToString().Length > 9)
                    compare = row["description"].ToString().Substring(0, 10);
                else
                    compare = row["description"].ToString();

                // comments
                if (doc.GetElementsByTagName("comments").Count > 0)
                {
                    for (int i = 0; i < doc.GetElementsByTagName("comments")[0].ChildNodes.Count; i++)
                    {
                        commentTimestamp = ReformatDate(doc.GetElementsByTagName("comments")[0].ChildNodes[i].Attributes["datecreated"].Value.Replace("T"," "));

                        commentOwner = doc.GetElementsByTagName("comments")[0].ChildNodes[i].ChildNodes[0].InnerText;

                        convertedName = ConvertToUser(commentOwner);
                        if (String.IsNullOrEmpty(convertedName))
                        {
                            // Don't modify commentOwner - it won't be matched by JIRA and will be replaced by
                            // the name of the logged-on user when importing. Instead, insert the name into the
                            // comment text.
                            commentText = "Submitted by " + commentOwner + Environment.NewLine + doc.GetElementsByTagName("comments")[0].ChildNodes[i].ChildNodes[2].InnerText;
                        }
                        else
                        {
                            commentOwner = convertedName;
                            commentText = doc.GetElementsByTagName("comments")[0].ChildNodes[i].ChildNodes[2].InnerText;
                        }

                        commentSubject = doc.GetElementsByTagName("comments")[0].ChildNodes[i].ChildNodes[1].InnerText;

                        // check that the first comment is not equal to the description of the bug
                        if (i == 0)
                        {
                            if (!commentText.StartsWith(compare))
                            {
                                //Console.WriteLine("Not identical. Bug: " + launchpadBugId.ToString());
                                row["comment" + (i + 2).ToString()] = commentTimestamp + ";" + commentOwner + ";" + commentSubject + System.Environment.NewLine + commentText;
                                lastInsertedComment = i + 2;
                            }
                            //else
                            //{
                            //    if (commentText.Equals(row["description"].ToString()))
                            //        Console.WriteLine("Identical. Bug: " + launchpadBugId.ToString());
                            //    else
                            //        Console.WriteLine("Start same. Bug: " + launchpadBugId.ToString()); // only email-addresses will defer in these cases because they are removed in description field
                            //}
                        }
                        else
                        {
                            row["comment" + (i + 2).ToString()] = commentTimestamp + ";" + commentOwner + ";" + commentSubject + System.Environment.NewLine + commentText;
                            lastInsertedComment = i + 2;
                        }
                    }
                }

                // messages
                if (doc.GetElementsByTagName("messages").Count > 0)
                {
                    for (int i = 0; i < doc.GetElementsByTagName("messages")[0].ChildNodes.Count; i++)
                    {
                        message = doc.GetElementsByTagName("messages")[0].ChildNodes[i].ChildNodes[0].InnerText;
                        messageTitle = doc.GetElementsByTagName("messages")[0].ChildNodes[i].ChildNodes[1].ChildNodes[0].InnerText;
                        messageFile = doc.GetElementsByTagName("messages")[0].ChildNodes[i].ChildNodes[1].ChildNodes[1].InnerText;
                        messageTimestamp = ReformatDate(doc.GetElementsByTagName("messages")[0].ChildNodes[i].Attributes["created"].Value);
                        messageOwner = doc.GetElementsByTagName("messages")[0].ChildNodes[i].Attributes["owner"].Value;

                        convertedName = ConvertToUser(messageOwner);
                        if (String.IsNullOrEmpty(convertedName))
                        {
                            // Don't modify messageOwner - it won't be matched by JIRA and will be replaced by
                            // the name of the logged-on user when importing. Instead, insert the name into the
                            // text.
                            messageTitle = "Submitted by " + messageOwner + System.Environment.NewLine + messageTitle;
                        }
                        else
                        {
                            messageOwner = convertedName;
                        }

                        row["comment" + (lastInsertedComment + i + 1).ToString()] = messageTimestamp + ";" + messageOwner + ";" + message + System.Environment.NewLine + messageTitle + System.Environment.NewLine + messageFile;
                    }
                }

                row["dateCreated"] = ReformatDate(doc.GetElementsByTagName("created")[0].InnerText);
                row["dateUpdated"] = ReformatDate(doc.GetElementsByTagName("date_last_updated")[0].InnerText);

                bugAssignee = doc.GetElementsByTagName("assignee")[0].InnerText;
                convertedName = ConvertToUser(bugAssignee);
                if (String.IsNullOrEmpty(convertedName) && !String.IsNullOrEmpty(bugAssignee.Trim()))
                {
                    // Don't modify bugOwner - it won't be matched by JIRA and will be replaced by
                    // the name of the logged-on user when importing. Instead, insert the name into the
                    // text, but only if we had an assignee in the first place (the IsNullOrEmpty test).
                    row["description"] = "Assigned to " + bugAssignee + System.Environment.NewLine + row["description"];
                    row["assignee"] = bugAssignee;
                }
                else
                    row["assignee"] = convertedName;

                // parse the nick from bug_owner_link
                bugOwner = doc.GetElementsByTagName("owner")[0].InnerText;
                convertedName = ConvertToUser(bugOwner);
                if (!String.IsNullOrEmpty(convertedName))
                    bugOwner = convertedName;

                row["owner"] = bugOwner;

                row["status"] = doc.GetElementsByTagName("status")[0].InnerText;

                if (row["status"].Equals("Fix Committed") || row["status"].Equals("Fix Released"))
                {
                    row["status"] = "Closed";
                    row["resolution"] = "Fixed";
                }
                else if (row["status"].Equals("Won't Fix"))
                {
                    row["status"] = "Closed";
                    row["resolution"] = "Won't Fix";
                }
                else if (row["status"].Equals("Invalid"))
                {
                    row["status"] = "Closed";
                    row["resolution"] = "Not a Bug";
                }
                else if (row["status"].Equals("Incomplete"))
                {
                    row["status"] = "Closed";
                    row["resolution"] = "Incomplete";
                }

                row["importance"] = doc.GetElementsByTagName("importance")[0].InnerText;

                if (row["importance"].Equals("Wishlist"))
                {
                    row["IssueType"] = "Task";
                    row["importance"] = "Minor";
                }
                else
                    row["IssueType"] = "Bug";

                row["milestone_title"] = doc.GetElementsByTagName("milestone_title")[0].InnerText;

                // attachments
                attachmentAmount = 0;

                for (int i = 0; i < doc.GetElementsByTagName("attachment").Count; i++)
                {
                    attachmentLink = doc.GetElementsByTagName("attachment")[i].Attributes["link"].Value;
                    attachmentFilename = doc.GetElementsByTagName("attachment")[i].ChildNodes[1].InnerText;
                    attachmentTimestamp = ReformatDate(doc.GetElementsByTagName("attachment")[i].ParentNode.Attributes["created"].Value);
                    attachmentOwner = doc.GetElementsByTagName("attachment")[i].ParentNode.Attributes["owner"].Value;
                    convertedName = ConvertToUser(attachmentOwner);
                    if (!String.IsNullOrEmpty(convertedName))
                        attachmentOwner = convertedName;
                    // JIRA accepts timestamp;author;filename;URL
                    row["attachment" + (i + 1).ToString()] = attachmentTimestamp + ";" + attachmentOwner + ";" + attachmentFilename + ";" + attachmentsTempURL + "attachment/" + System.Web.HttpUtility.UrlEncode(attachmentFilename);
                    attachmentAmount++;
                }

                // add xml export of the bug itself as attachment
                row["attachment" + (attachmentAmount + 1).ToString()] = DateTime.Now.ToString("yyyyMMddHHmmss") + ";"+bugOwner+";" + "LPexportBug" + launchpadBugId + ".xml" + ";" + attachmentsTempURL + "LPexportBug" + launchpadBugId + ".xml";

                table.Rows.Add(row);

                if (debug)
                {
                    debugCount++;

                    if (debugCount == 100)
                    break;
                }
            }

            Console.WriteLine("Table row count: " + table.Rows.Count);
            Console.WriteLine("File amount: " + dir.GetFiles().Length);

            // create csv
            StreamWriter stream = new StreamWriter(dir.FullName + @"\launchpad-bugs-export.csv");

            if (includeHeader)
            {
                string caption = "";

                for (int i = 0; i < table.Columns.Count; i++)
                {
                    caption = table.Columns[i].Caption;

                    if (caption.StartsWith("comment"))
                        caption = "comment";
                    else if (caption.StartsWith("attachment"))
                        caption = "attachment";

                    WriteItem(stream, caption, true);

                    if (i < table.Columns.Count - 1)
                        stream.Write(',');
                    else
                        stream.WriteLine();
                }
            }

            foreach (DataRow row2 in table.Rows)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    WriteItem(stream, row2[i], true);

                    if (i < table.Columns.Count - 1)
                        stream.Write(',');
                    else
                        stream.Write('\n');
                }

            }

            stream.Flush();
            stream.Close();

            if (unmatchedNames.Count != 0)
            {
                Console.WriteLine("The following LP names were not matched. You may want to create");
                Console.WriteLine("dummy accounts on JIRA to improve the import process.");
                Console.WriteLine("");
                unmatchedNames.Sort();
                foreach (string name in unmatchedNames)
                    Console.WriteLine(" * {0}", name);
            }

            Console.WriteLine("Press RETURN to finish");
            Console.ReadLine();
        }

        private static void WriteItem(TextWriter stream, object item, bool quoteall)
        {
            if (item == null)
                return;

            string s = item.ToString();

            if (quoteall || s.IndexOfAny("\",\x0A\x0D".ToCharArray()) > -1)
                stream.Write("\"" + s.Replace("\"", "\"\"") + "\"");
            else
                stream.Write(s);

            stream.Flush();
        }
    }

    public class jira_user
    {
        public string self { get; set; }
        public string name { get; set; }
        public string emailAddress { get; set; }
        public URLpairs avatarUrls { get; set; }
        public string displayName { get; set; }
        public string active { get; set; }
        public string timeZone { get; set; }
    }

    public class URLpairs
    {
        public string size { get; set; }
        public string url { get; set; }
    }
}
