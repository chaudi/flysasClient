﻿using FlysasLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace FlysasClient
{
    public class ConsoleClient
    {
        SASRestClient client;
        Options options;
        System.IO.TextWriter txtOut = Console.Out;
        System.IO.TextReader txtIn = Console.In;
        OpenFlightsData.OFData data;

        enum Commands
        {
            Login, History, Logout, Points, Set, Help, Options, Export, Info, Quit, Calendar
        };

        HashSet<Commands> requiresLogin = new HashSet<Commands>() { Commands.History, Commands.Points, Commands.Export, Commands.Calendar };

        public ConsoleClient(Options options, OpenFlightsData.OFData data)
        {
            var httpClient = new HttpClient(
                new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                }
                );
            this.client = new SASRestClient(httpClient);
            this.options = options;
            this.data = data;
        }

        public async System.Threading.Tasks.Task InputLoop()
        {
            string input = null;
            txtOut.WriteLine("Syntax: Origin-Destination outDate [inDate]");
            while (!nameof(Commands.Quit).Equals(input, StringComparison.OrdinalIgnoreCase))
            {
                input = ReadLine.Read("FS> ");
                if (input != "")
                {
                    ReadLine.AddHistory(input);
                    await Run(input);
                }
            }
        }

        public async System.Threading.Tasks.Task Run(string input)
        {
            var parser = new Parser();
            foreach (string query in input.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Command(query))
                {
                    SASQuery req = null;
                    try
                    {
                        req = parser.Parse(query.Trim());
                        req.Mode = options.Mode;
                    }
                    catch (ParserException ex)
                    {
                        txtOut.WriteLine("Syntax error:" + ex.Message);
                    }
                    catch
                    {
                        txtOut.WriteLine("Syntax error:");
                    }
                    if (req != null)
                    {
                        var outDateStart = req.OutDate.Value;
                        var inDateStart = req.InDate;
                        for (var i = 0; i < options.Days; i++)
                        {
                            req.OutDate = outDateStart.AddDays(i);
                            if (inDateStart.HasValue)
                                req.InDate = inDateStart.Value.AddDays(i);
                            
                            SearchResult result = null;
                            try
                            {
                                result = await client.SearchAsync(req);
                            }
                            catch
                            {
                                txtOut.WriteLine("Error");
                            }
                            if (result != null)
                            {
                                if (result.errors != null && result.errors.Any())
                                    txtOut.WriteLine("flysas.com says: " + result.errors.First().errorMessage);
                                else
                                {
                                    var printer = new TablePrinter(txtOut);
                                    txtOut.WriteLine("********* Outbound " + req.OutDate.Value.ToString("yyyy-MM-dd") + " *******");
                                    printer.PrintFlights(result.outboundFlights, options, req.From, req.To);
                                    if (req.InDate.HasValue)
                                    {
                                        txtOut.WriteLine("********* Inbound " + req.InDate.Value.ToString("yyyy-MM-dd") + " *******");
                                        printer.PrintFlights(result.inboundFlights, options, req.To, req.From);
                                    }
                                }
                            }
                            txtOut.Write(Environment.NewLine + Environment.NewLine);
                        }
                    }
                }
            }
        }

        bool Command(string input)
        {
            var names = Enum.GetNames(typeof(Commands));
            var stack = new CommandStack(input);
            if (stack.Any())
            {
                var sCmd = stack.Pop();
                var name = names.FirstOrDefault(s => s.Equals(sCmd, StringComparison.OrdinalIgnoreCase));
                if (name != null)
                {
                    Commands cmd = (Commands)Enum.Parse(typeof(Commands), name);
                    if (!client.LoggedIn && requiresLogin.Contains(cmd))
                    {
                        txtOut.WriteLine("This feature requires login");
                        return true;
                    }
                    switch (cmd)
                    {
                        case Commands.Set:
                            if (!options.Set(stack))
                                txtOut.WriteLine("Error: Unknown settings");
                            break;
                        case Commands.Info:
                            info(stack);
                            break;
                        case Commands.Login:
                            login(stack);
                            break;
                        case Commands.Export:
                            history(stack, true);
                            break;
                        case Commands.History:
                            history(stack, false);
                            break;
                        case Commands.Points:
                            points();
                            break;
                        case Commands.Options:
                            txtOut.Write(options.Help() + Environment.NewLine);
                            break;
                        case Commands.Help:
                            txtOut.WriteLine("Commands:");
                            foreach (var s in names)
                                txtOut.WriteLine("\t" + s);
                            break;
                        case Commands.Logout:
                            client.Logout();
                            break;
                        case Commands.Quit:
                            client.Logout();
                            Environment.Exit(0);
                            break;
                        case Commands.Calendar:
                            Calendar();
                            break;
                    }
                    return true;
                }
            }
            return false;
        }

        private void Calendar()
        {
            ReservationsResult.Reservations reservations = client.MyReservations();
            if (reservations.ReservationsReservations.Any())
            {
                foreach (ReservationsResult.Reservation reservation in reservations.ReservationsReservations)
                {
                    txtOut.Write("Booking reference: ");
                    txtOut.WriteLine(reservation.AirlineBookingReference);
                    txtOut.Write($"Destination: {reservation.Connections[0].Destination.AirportCode}");
                    txtOut.WriteLine($" Was written to your export folder as {reservation.AirlineBookingReference}.ICS");
                    txtOut.WriteLine("Just drag it into your calender app.");

                    FlysasLib.CalendarPrinter cp = new CalendarPrinter();
                    cp.WriteICal(reservation);

                }

            }
            else
                txtOut.WriteLine("Sorry: No bookings found!");
        }

        private void points()
        {
            try
            {
                var res = client.History(1);
                txtOut.WriteLine("Status: " + res.eurobonus.currentTierName);
                txtOut.WriteLine(res.eurobonus.totalPointsForUse + " points for use");
                txtOut.WriteLine(res.eurobonus.pointsAvailable + " basic points earned this period");
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                txtOut.WriteLine("Error getting info");
                txtOut.WriteLine(ex.Message);
            }
        }

        private void info(CommandStack stack)
        {
            if (stack.Any())
            {

                var arglist = stack.ToList();
                var s = string.Join(" ", arglist);
                var airport = data.Airports.FirstOrDefault(ap => ap.IATA == s.ToUpper());
                if (airport != null)
                {
                    txtOut.WriteLine($"Airport: {airport.IATA}/{airport.ICAO}");
                    txtOut.WriteLine($"Name: {airport.Name}");
                    txtOut.WriteLine($"City: {airport.City}");
                    txtOut.WriteLine($"Country: {airport.Country}");
                    txtOut.WriteLine($"Type: {airport.Type}");
                    txtOut.WriteLine($"Timezone {airport.Timezone}");
                    txtOut.WriteLine($"Daylight saving time: {airport.DST}");
                }
                var cities = data.Airports.Where(ap => s.Equals(ap.City, StringComparison.OrdinalIgnoreCase)).ToList();
                if (cities.Any())
                {
                    txtOut.WriteLine("Airports in " + s);
                    foreach (var c in cities)
                        txtOut.WriteLine("\t" + c.IATA + ": " + c.Name);
                }
                var airline = data.Airlines.FirstOrDefault(al => s.Equals(al.Name, StringComparison.OrdinalIgnoreCase) || s.ToUpper() == al.IATA || s.ToUpper() == al.ICAO);
                if (airline != null)
                {
                    txtOut.WriteLine("Airline info for " + s);
                    txtOut.WriteLine($"\t{airline.IATA}/{airline.ICAO}");
                    txtOut.WriteLine($"\tName: {airline.Name}");
                    txtOut.WriteLine($"\tCallsign: {airline.Callsign}");
                    txtOut.WriteLine($"\tCountry: {airline.Country}");
                }
                var plane = data.Planes.FirstOrDefault(p => s.ToUpper() == p.IATA || s.ToUpper() == p.ICAO);
                if (plane != null)
                {
                    txtOut.WriteLine("Airplane info for " + s);
                    txtOut.WriteLine($"\t{plane.IATA}/{plane.ICAO}");
                    txtOut.WriteLine($"\tName: {plane.Name}");
                }
                if (arglist.Count >= 2)
                {

                    var orig = arglist[0];
                    var dest = arglist[1] == "-" && arglist.Count > 2 ? arglist[2] : arglist[1];
                    var routeList = data.Routes.Where(r => r.FromIATA == orig.ToUpper() && r.ToIATA == dest.ToUpper()).ToList();
                    if (routeList.Any())
                    {
                        txtOut.WriteLine($"Routes from {orig} to {dest}");
                        foreach (var r in routeList)
                            txtOut.WriteLine("\t" + r.AirlineCode + (r.CodeShare ? " codeshare " : ""));
                    }
                }
            }
        }

        private void history(CommandStack stack, bool export)
        {
            int page = 1;
            int pages = 1;
            bool fetchAll = true;
            List<Transaction> all = new List<Transaction>();
            TransactionRoot res = null;

            if (stack.Any())
            {
                if (int.TryParse(stack.Pop(), out page) && !stack.Any())
                    fetchAll = false;
                else
                {
                    txtOut.WriteLine("Parser error");
                    return;
                }
            }
            txtOut.WriteLine("");
            do
            {
                txtOut.Write($"\rFetching page { page}{(pages > 1 ? " of " + pages : "")}");
                try
                {
                    res = client.History(page);
                }
                catch (Exception ex)
                {
                    txtOut.WriteLine($"Error getting page {page}");
                    txtOut.WriteLine(ex.Message);
                }
                if (res.errors != null)
                {
                    txtOut.WriteLine($"Error getting page {page} {res.errors.First().errorMessage}");
                }
                page++;
                if (fetchAll)
                    pages = res.eurobonus.transactionHistory.totalNumberOfPages;
                if (res.errors == null && res.eurobonus != null && res.eurobonus.transactionHistory.transaction != null)
                    all.AddRange(res.eurobonus.transactionHistory.transaction);
            } while (page <= pages);
            txtOut.Write("\r");
            if (export)
            {
                var exporter = new FlightExporter();
                var list = exporter.Convert(all);
                txtOut.WriteLine($"Found {list.Count} flights");
                if (list.Any())
                    exporter.SaveCSV(list);
                txtOut.WriteLine("Files saved");
            }
            else
            {
                Table t = new Table();
                foreach (var r in all)
                {
                    var values = new List<string>();
                    values.Add(r.datePerformed.ToString("yyyy-MM-dd"));
                    values.Add(r.typeOfTransaction);
                    values.Add(r.basicPointsAfterTransaction);
                    values.Add(r.availablePointsAfterTransaction.ToString());
                    values.Add(r.description1 + " " + r.description2);

                    t.Rows.Add(values);

                }

                t.Alignment[3] = TextAlignment.Right;
                t.Print(txtOut);
                if (fetchAll)
                {
                    txtOut.WriteLine("Summary");
                    t = new Table();
                    foreach (var g in all.GroupBy(trans => trans.typeOfTransaction))
                        t.Rows.Add(new List<string>(new[] { g.Key, g.Sum(trans => trans.availablePointsAfterTransaction).ToString() }));
                    t.Alignment[1] = TextAlignment.Right;
                    t.Print(txtOut);
                }
            }
        }

        private void login(CommandStack stack)
        {
            var userName = options.UserName;
            var passWord = options.Password;
            if (userName.IsNullOrWhiteSpace())
                if (stack.Any())
                    userName = stack.Pop();
                else
                {
                    txtOut.WriteLine("User: ");
                    userName = txtIn.ReadLine();
                }

            if (passWord.IsNullOrWhiteSpace())
                if (stack.Any())
                    passWord = stack.Pop();
                else
                {
                    txtOut.WriteLine("Enter password: ");
                    passWord = getPassword();
                }
            try
            {
                var result = client.Login(userName, passWord);
                txtOut.WriteLine($"Login for {userName}  {(result ? " success" : "failed")}");
            }
            catch (Exception)
            {
                txtOut.WriteLine("Login failed");
            }
        }

        private string getPassword()
        {
            string str = "";
            ConsoleKeyInfo key;
            while (true)
            {
                key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (str.Any())
                    {
                        str = str.Substring(0, str.Length - 1);
                        txtOut.Write("\b \b");
                    }
                }
                else
                {
                    if (key.Key == ConsoleKey.Enter)
                        break;
                    else
                    {
                        str += key.KeyChar;
                        txtOut.Write("*");
                    }
                }
            }
            txtOut.WriteLine();
            return str;
        }
    }
}