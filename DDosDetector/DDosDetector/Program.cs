using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DDosDetector
{
    class Program
    {
        public class DataInFile
        {
            public decimal time = 0M;
            public string source = string.Empty;
            public string destination = string.Empty;
            public string protocol = string.Empty;
            public int length = 0;

            public static DataInFile FromCsv(string csvLine)
            {
                string[] values = csvLine.Split(',');
                DataInFile dataInFile = new DataInFile();
                dataInFile.time = Math.Floor(Convert.ToDecimal(values[1].Trim('"')));
                dataInFile.source = Convert.ToString(values[2].Trim('"'));
                dataInFile.destination = Convert.ToString(values[3].Trim('"'));
                dataInFile.protocol = Convert.ToString(values[4].Trim('"'));
                dataInFile.length = Convert.ToInt32(values[5].Trim('"'));
                return dataInFile;
            }
        }

        public class DataForFuzzy
        {
            public int jlhData = 0;
            public int jlhSource = 0;
            public int jlhLength = 0;
        }

        public class DataFuzzy
        {
            public string kategori = string.Empty;
            public int nilai = 0;
        }

        static void Main(string[] args)
        {
            string filePath = "../../Dataset/normal.csv";
            //string filePath = "../../Dataset/data_ddos.csv";

            List<DataInFile> dataInFileList = File.ReadAllLines(filePath).Skip(1).Select(v => DataInFile.FromCsv(v)).ToList();

            List<DataInFile> icmpDataList = dataInFileList.Where(x => x.protocol.ToUpper() == "ICMP".ToUpper()).ToList();
            List<string> destinationList = icmpDataList.Select(x => x.destination).Distinct().ToList();
            List<decimal> timeList = icmpDataList.Select(x => x.time).Distinct().ToList();

            List<List<string>> hasilPrediksi = new List<List<string>>();
            List<string> dataPrediksi = new List<string>();
            dataPrediksi.Add("Time");
            dataPrediksi.Add("Source");
            dataPrediksi.Add("Destination");
            dataPrediksi.Add("Banyak paket");
            dataPrediksi.Add("Hasil Prediksi");
            hasilPrediksi.Add(dataPrediksi);

            foreach (decimal time in timeList)
            {
                foreach (string destination in destinationList)
                {
                    DataForFuzzy dataForFuzzy = new DataForFuzzy();
                    dataForFuzzy.jlhData = icmpDataList.Where(x => x.time == time && x.destination.ToUpper() == destination.ToUpper()).ToList().Count;
                    List<string> dataSource = icmpDataList.Where(x => x.time == time && x.destination.ToUpper() == destination.ToUpper()).Select(y => y.source).ToList();
                    dataForFuzzy.jlhSource = dataSource.Count;
                    dataForFuzzy.jlhLength = icmpDataList.Where(x => x.time == time && x.destination.ToUpper() == destination.ToUpper()).Select(y => y.length).ToList().Sum();

                    decimal nilaiPrediksi = DDosDetectorUsingFuzzyLogic(dataForFuzzy);

                    //thresholding
                    dataPrediksi = new List<string>();
                    dataPrediksi.Add(time.ToString());
                    dataPrediksi.Add(String.Join("; ", dataSource));
                    dataPrediksi.Add(destination.ToString());
                    dataPrediksi.Add(dataForFuzzy.jlhLength.ToString());
                    if (nilaiPrediksi > (decimal)0.4)
                        dataPrediksi.Add("1");
                    else
                        dataPrediksi.Add("0");

                    hasilPrediksi.Add(dataPrediksi);
                }
            }

            //Save hasil prediksi ke file
            File.WriteAllLines("../../Dataset/Prediksi/hasilPrediksi_normal.csv", hasilPrediksi.Select(x => string.Join(",", x)));
            //File.WriteAllLines("../../Dataset/Prediksi/hasilPrediksi_data_ddos.csv", hasilPrediksi.Select(x => string.Join(",", x)));
        }

        private static decimal DDosDetectorUsingFuzzyLogic(DataForFuzzy dataForFuzzy)
        {
            decimal nilaiPrediksi = 0M;

            #region Fuzzifikasi
            List<DataFuzzy> f_jlhData = MF_JlhData(dataForFuzzy.jlhData);
            List<DataFuzzy> f_jlhSource = MF_JlhSource(dataForFuzzy.jlhSource);
            List<DataFuzzy> f_jlhLength = MF_JlhLength(dataForFuzzy.jlhLength);
            #endregion

            #region Inference : Conjunction-Disjunction (Min-Max)
            //Conjunction (Min)
            List<DataFuzzy> hasilConjunction = new List<DataFuzzy>();
            foreach(DataFuzzy dt_JlhData in f_jlhData)
            {
                foreach(DataFuzzy dt_jlhSource in f_jlhSource)
                {
                    foreach(DataFuzzy dt_JlhLength in f_jlhLength)
                    {
                        DataFuzzy dataInference = InferenceProcess(dt_JlhData, dt_jlhSource, dt_JlhLength);
                        hasilConjunction.Add(dataInference);
                    }
                }
            }

            //Disjunction (Max)
            List<DataFuzzy> hasilDisjuntion = new List<DataFuzzy>();

            DataFuzzy rendah = new DataFuzzy();
            rendah.kategori = "Rendah";
            rendah.nilai = hasilConjunction.Where(x => x.kategori.ToUpper() == rendah.kategori.ToUpper()).Select(y => y.nilai).ToList().Max();
            hasilDisjuntion.Add(rendah);

            DataFuzzy tinggi = new DataFuzzy();
            tinggi.kategori = "Tinggi";
            tinggi.nilai = hasilConjunction.Where(x => x.kategori.ToUpper() == tinggi.kategori.ToUpper()).Select(y => y.nilai).ToList().Max();
            hasilDisjuntion.Add(tinggi);
            #endregion

            #region Defuzzifikasi : Weighted Average
            foreach(DataFuzzy dataDefuzzy in hasilDisjuntion)
            {
                if (dataDefuzzy.kategori.ToUpper() == "Rendah".ToUpper())
                    nilaiPrediksi += dataDefuzzy.nilai * 0;
                else if (dataDefuzzy.kategori.ToUpper() == "Tinggi".ToUpper())
                    nilaiPrediksi += dataDefuzzy.nilai * 1;
            }

            decimal totalValue = hasilDisjuntion.Select(x => x.nilai).Sum();
            if (totalValue == 0)
                nilaiPrediksi = 0;
            else
                nilaiPrediksi /= totalValue;
            #endregion

            return nilaiPrediksi;
        }

        private static DataFuzzy InferenceProcess(DataFuzzy dt_JlhData, DataFuzzy dt_jlhSource, DataFuzzy dt_JlhLength)
        {
            DataFuzzy dataInference = new DataFuzzy();
            if (dt_JlhData.kategori.ToUpper() == "Sedikit".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Single".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Pendek".ToUpper())
                dataInference.kategori = "Rendah";
            else if (dt_JlhData.kategori.ToUpper() == "Sedikit".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Single".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Normal".ToUpper())
                dataInference.kategori = "Tinggi";
            else if (dt_JlhData.kategori.ToUpper() == "Sedikit".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Single".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Panjang".ToUpper())
                dataInference.kategori = "Tinggi";
            else if (dt_JlhData.kategori.ToUpper() == "Sedikit".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Multi".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Pendek".ToUpper())
                dataInference.kategori = "Rendah";
            else if (dt_JlhData.kategori.ToUpper() == "Sedikit".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Multi".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Normal".ToUpper())
                dataInference.kategori = "Tinggi";
            else if (dt_JlhData.kategori.ToUpper() == "Sedikit".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Multi".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Panjang".ToUpper())
                dataInference.kategori = "Tinggi";
            else if (dt_JlhData.kategori.ToUpper() == "Banyak".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Single".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Pendek".ToUpper())
                dataInference.kategori = "Tinggi";
            else if (dt_JlhData.kategori.ToUpper() == "Banyak".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Single".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Normal".ToUpper())
                dataInference.kategori = "Tinggi";
            else if (dt_JlhData.kategori.ToUpper() == "Banyak".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Single".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Panjang".ToUpper())
                dataInference.kategori = "Tinggi";
            else if (dt_JlhData.kategori.ToUpper() == "Banyak".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Multi".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Pendek".ToUpper())
                dataInference.kategori = "Tinggi";
            else if (dt_JlhData.kategori.ToUpper() == "Banyak".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Multi".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Normal".ToUpper())
                dataInference.kategori = "Tinggi";
            else if (dt_JlhData.kategori.ToUpper() == "Banyak".ToUpper()
                && dt_jlhSource.kategori.ToUpper() == "Multi".ToUpper()
                && dt_JlhLength.kategori.ToUpper() == "Panjang".ToUpper())
                dataInference.kategori = "Tinggi";

            dataInference.nilai = Math.Min(dt_JlhData.nilai, Math.Min(dt_jlhSource.nilai, dt_JlhLength.nilai));
            return dataInference;
        }

        private static List<DataFuzzy> MF_JlhLength(int jlhLength)
        {
            List<DataFuzzy> hasilMembershipFunction = new List<DataFuzzy>();
            DataFuzzy pendek = new DataFuzzy(); pendek.kategori = "Pendek";
            DataFuzzy normal = new DataFuzzy(); normal.kategori = "Normal";
            DataFuzzy panjang = new DataFuzzy(); panjang.kategori = "Panjang";

            if (jlhLength <= 60)
            {
                pendek.nilai = 1;
                normal.nilai = 0;
                panjang.nilai = 0;
            }
            else if (jlhLength > 60 && jlhLength < 90)
            {
                pendek.nilai = (90 - jlhLength) / (90-60);
                normal.nilai = (jlhLength - 60) / (90 - 60);
                panjang.nilai = 0;
            }
            else if (jlhLength >= 90 && jlhLength < 120)
            {
                pendek.nilai = 0;
                normal.nilai = (120 - jlhLength) / (120 - 90);
                panjang.nilai = (jlhLength - 90) / (120 - 90);
            }
            else
            {
                pendek.nilai = 0;
                normal.nilai = 0;
                panjang.nilai = 1;
            }

            hasilMembershipFunction.Add(pendek);
            hasilMembershipFunction.Add(normal);
            hasilMembershipFunction.Add(panjang);

            return hasilMembershipFunction;
        }

        private static List<DataFuzzy> MF_JlhSource(int jlhSource)
        {
            List<DataFuzzy> hasilMembershipFunction = new List<DataFuzzy>();
            DataFuzzy single = new DataFuzzy(); single.kategori = "Single";
            DataFuzzy multi = new DataFuzzy(); multi.kategori = "Multi";

            if (jlhSource <= 1)
            {
                single.nilai = 1;
                multi.nilai = 0;
            }
            else if (jlhSource > 1 && jlhSource < 2)
            {
                single.nilai = (2 - jlhSource) / (2 - 1);
                multi.nilai = (jlhSource - 1) / (2 - 1);
            }
            else
            {
                single.nilai = 0;
                multi.nilai = 1;
            }

            hasilMembershipFunction.Add(single);
            hasilMembershipFunction.Add(multi);

            return hasilMembershipFunction;
        }

        private static List<DataFuzzy> MF_JlhData(int jlhData)
        {
            List<DataFuzzy> hasilMembershipFunction = new List<DataFuzzy>();
            DataFuzzy sedikit = new DataFuzzy(); sedikit.kategori = "Sedikit";
            DataFuzzy banyak = new DataFuzzy(); banyak.kategori = "Banyak";

            if (jlhData <= 10)
            {
                sedikit.nilai = 1;
                banyak.nilai = 0;
            }
            else if (jlhData > 10 && jlhData < 50)
            {
                sedikit.nilai = (50 - jlhData) / (50 - 10);
                banyak.nilai = (jlhData - 10) / (50 - 10);
            }
            else
            {
                sedikit.nilai = 0;
                banyak.nilai = 1;
            }

            hasilMembershipFunction.Add(sedikit);
            hasilMembershipFunction.Add(banyak);

            return hasilMembershipFunction;
        }
    }
}
