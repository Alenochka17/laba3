/*----------------------------------------------------------------------+
 |	filename:	R-Star.cs                                               |
 |----------------------------------------------------------------------|
 |	version:	1.00                                                    |
 |	created:    19/09/2006	12:59                                       |
 |  author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	������ � �������                                        |
 +----------------------------------------------------------------------+
 |  ���������������� ����������� �� ���� RAID ������                    |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{
    class Program
    {
        static void OnUpdateFileSplittingProgress(double progress)
        {
            Console.Write(" ��������� �����: {0} %\r", Convert.ToInt32(progress));
        }

        static void OnFileSplittingFinish()
        {
            Console.WriteLine(" ��������� ����� ���������");
        }

        static void OnUpdateRSMatrixFormingProgress(double progress) 
        {
            Console.Write(" ������ ������� �����������: {0} %\r", Convert.ToInt32(progress));
        }

        static void OnRSMatrixFormingFinish()
        {
            Console.WriteLine(" ������ ������� ����������� ��������");
        }

        static void OnUpdateFileCodingProgress(double progress)
        {
            Console.Write(" ����������� ����-��������: {0} %\r", Convert.ToInt32(progress));
        }

        static void OnFileCodingFinish()
        {
            Console.WriteLine(" ����������� ����-�������� ���������");
        }

        static void OnUpdateFileAnalyzeProgress(double progress)
        {
            Console.Write(" �������� ����������� ������: {0} %\r", Convert.ToInt32(progress));
        }

        static void OnFileAnalyzeFinish()
        {
            Console.WriteLine(" �������� ����������� ������ ��������");
        }

        static void OnGetDamageStat(double percOfDamage, double percOfAltEcc)
        {
            if (
                    (percOfDamage == -1)
                 &&
                    (percOfAltEcc == -1)
                )
            {
                Console.WriteLine("     ������: ��� �������� ���� ����������! �������������� ������ ����������.");
                
                return;
            }

            if (
                    (percOfDamage == 0)
                 &&
                    (percOfAltEcc == -1)
                )
            {
                Console.WriteLine(" ����������� ����� �� ����� ����������� � ������� �������� �����!");
                Console.WriteLine("");

                return;
            }

            Console.WriteLine(" ����� ������������ �����: {0} %, ������ ����� ��� ��������������: {1} %", Convert.ToInt32(percOfDamage), Convert.ToInt32(percOfAltEcc));
            Console.WriteLine("");
        }

        /// <summary>
        /// ����� �������� ������ �� ��, ��� ��� �������� ������ ���������� �������
        /// </summary>
        /// <returns>�������� �� ������ ���������� Int-���������</returns>
        static bool IsIntString(String str)
        {            
            // ���� ����� ������ ������ 10 �������� - �������
            if (str.Length > 10)
            {
                return false;
            }

            // ��������� �� ���� �������� ������...
            for (int i = 0; i < str.Length; i++)
            {
                //...�, ���� ���� �� ���� ������ ������� �� ���������� �������
                // ����� ��� ���� - �������� �� ����
                if (
                        (str[i] < 0x30)
                    ||
                        (str[i] > 0x39)
                    )
                {
                    return false;
                }
            }

            return true;
        }

        static void Main(string[] args)
        {
            Console.WriteLine("_______________________________________________________________________________");
            Console.WriteLine("");
            Console.WriteLine(" R-Star 1.00 D               Copyright (C) 2006 DrAF              19 Sept 2006 ");
            Console.WriteLine("_______________________________________________________________________________");
            Console.WriteLine("");

            // ��������� �� ������������ ������� ������ ����������
            if (args.Length != 1)
            {
                Console.WriteLine(" ������: R-Star <��� �����>");

                return;
            }

            // ������� ��������� ������ ���� RecoveryStar
            RecoveryStarCore eRecoveryStarCore = new RecoveryStarCore();

            // ������� ��������� ������ ��� �������� (����������) ����� ����� � ���������� ������
            FileNamer eFileNamer = new FileNamer();

            // ������������� �� ��������� ���������
            eRecoveryStarCore.OnUpdateFileSplittingProgress = new OnUpdateFileSplittingProgressHandler(OnUpdateFileSplittingProgress);
            eRecoveryStarCore.OnFileSplittingFinish = new OnFileSplittingFinishHandler(OnFileSplittingFinish);

            eRecoveryStarCore.OnUpdateRSMatrixFormingProgress = new OnUpdateRSMatrixFormingProgressHandler(OnUpdateRSMatrixFormingProgress);
            eRecoveryStarCore.OnRSMatrixFormingFinish = new OnRSMatrixFormingFinishHandler(OnRSMatrixFormingFinish);
            eRecoveryStarCore.OnUpdateFileCodingProgress = new OnUpdateFileCodingProgressHandler(OnUpdateFileCodingProgress);
            eRecoveryStarCore.OnFileCodingFinish = new OnFileCodingFinishHandler(OnFileCodingFinish);
            
            eRecoveryStarCore.OnUpdateFileAnalyzeProgress = new OnUpdateFileAnalyzeProgressHandler(OnUpdateFileAnalyzeProgress);
            eRecoveryStarCore.OnFileAnalyzeFinish = new OnFileAnalyzeFinishHandler(OnFileAnalyzeFinish);
            eRecoveryStarCore.OnGetDamageStat = new OnGetDamageStatHandler(OnGetDamageStat);

            // ��� ����� ��� ���������
            String filename = eFileNamer.GetShortFilename(args[0]);

            // ���� � ������ ��� ���������
            String path = eFileNamer.GetPath(args[0]);

            // ���������� �������� �����
            int dataCount = 0;

            // ���������� ����� ��� ��������������
            int eccCount = 0;

            // ����� ���������� �����
            int allVolCount = 0;

            // ������������ � ���������
            int redundancy = 0;

            // ������, ��������� �������������
            String str;

            // ��������� ��� �����
            String tempFilename = filename;

            // ���� ��� ����� ����� ���� ��������� ����������� �� ����������� �������,
            // �� ���� ����������� ��������� ������-�����
            if (eFileNamer.Unpack(ref tempFilename, ref dataCount, ref eccCount))
            {      
                Console.WriteLine(" �������������� ������ �� ����������������� ������ �����...");
                Console.WriteLine("");

                // ��������� �������������� ������
                eRecoveryStarCore.StartToRecover(path, filename, true);                

            } else
            {
                // ���������� ������������, ��� ���� �����������
                bool inputOK = false;

                while (!inputOK)
                {
                    Console.Write(" ������� ����� ���������� �����: ");

                    // ��������� ������ ������
                    str = Console.ReadLine();

                    // ���� ������������ ���� ������ ������ - ������������� �������� ��-���������
                    if (str == "")
                    {
                        str = "1024";
                        Console.WriteLine("     ����������� ����� ���������� ����� ��-���������: 1024");
                        Console.WriteLine("");

                    } else
                    {
                        // ���� ���������� ������ ��������� ������������ ��������� "Int",
                        // ��������� �� ��������� �������� �����
                        if (!IsIntString(str))
                        {                         
                            Console.WriteLine("     ������: ��������� ������ �������� ������������ ������� ��� ������� �������!");
                            Console.WriteLine("");

                            continue;
                        }
                    }

                    // ���� ��� ��������� - ������������� ��������
                    allVolCount = Convert.ToInt32(str);

                    // ���������� �������� �� ���������� ���������� �����
                    if (
                            (allVolCount >= 3)
                         &&
                            (allVolCount <= 16384)
                        )
                    {
                        inputOK = true;

                    } else
                    {
                        Console.WriteLine("     ������: ����� ���������� ����� ������ ���������� � ��������� 3..16384!");
                        Console.WriteLine("");                        
                    }
                }

                // ���������� ������������, ��� ���� �����������
                inputOK = false;

                while (!inputOK)
                {
                    Console.Write(" ������� ������������ ����������� � ���������: ");

                    // ��������� ������ ������
                    str = Console.ReadLine();

                    // ���� ������������ ���� ������ ������ - ������������� �������� ��-���������
                    if (str == "")
                    {
                        str = "50";
                        Console.WriteLine("     ����������� ������������ ����������� ��-���������: 50 %");
                    
                    } else
                    {
                        // ���� ���������� ������ ��������� ������������ ��������� "Int",
                        // ��������� �� ��������� �������� �����
                        if (!IsIntString(str))
                        {                            
                            Console.WriteLine("     ������: ��������� ������ �������� ������������ ������� ��� ������� �������!");
                            Console.WriteLine("");

                            continue;
                        }
                    }

                    // ������������� ��������
                    redundancy = Convert.ToInt32(str);

                    // ��������� ������������� �������� �� ������������
                    if (
                            (redundancy > 0)
                         &&
                            (redundancy <= 100)
                        )
                    {
                        inputOK = true;

                    } else
                    {                        
                        Console.WriteLine("     ������: ������������ ����������� ������ ���������� � ��������� 1..100 %!");
                        Console.WriteLine("");
                    }
                }
                
                // ���������� �������� ���������� ��������� �� ���
                double percByVol = (double)allVolCount / (double)200;

                // ��������� ���������� ����� ��� ��������������
                eccCount = (int)((double)redundancy * percByVol); // ����� ��� ��������������
               
                // � ������ ������������� ������������ ���������� ����� ��� ��������������
                if (eccCount < 1)
                {
                    eccCount = 1;
                }
                
                // ���������� �������� ����� ������� �� ����������� ��������
                dataCount = allVolCount - eccCount;

                // ���� ���������� �����
                while (eccCount >= dataCount)
                {
                    eccCount--;
                    dataCount++;
                }
                
                Console.WriteLine("");
                Console.WriteLine(" ������� ��������� ���������: ");
                Console.WriteLine(" �������� �����: {0}", dataCount);
                Console.WriteLine(" ����� ��� ��������������: {0}", eccCount);
                Console.WriteLine("");
                Console.WriteLine("===============================================================================");
                Console.WriteLine("");
                Console.WriteLine(" �������� ����������������� ������ �����...");
                Console.WriteLine("");

                // ��������� ���������������� �����������
                eRecoveryStarCore.StartToProtect(path, filename, dataCount, eccCount, true);
            }

            // ���� ��������� ���������
            while (eRecoveryStarCore.InProcessing)
            {
                Thread.Sleep(1000);
            }

            // � ����� � ��������� �������� ���������� �������� �������
            // ���������� ��������� ������ ���������, ��������� �������
            // ����������� � ���� ������. ����� ��� �� ��������, ��
            // ������������� �� ��������� ��������, ��������, ���
            // "�� ����������". ���������� ��������� ��������,
            // �� ���� - ���� �������.
            for (int i = 0; i < 900; i++)
            {
                if (!eRecoveryStarCore.Finished)
                {
                    Thread.Sleep(1000);
                }
            }
                        
            Console.WriteLine("_______________________________________________________________________________");
            Console.WriteLine("");
            
            // ��������� �� ������������ ���������� ���������
            if (eRecoveryStarCore.ProcessedOK)
            {
                Console.WriteLine(" ��������� ������ ��������� �������!");

            } else
            {                
                Console.WriteLine(" ������ ��� ������ � �������!");
            }
        }
    }
}