/*----------------------------------------------------------------------+
 |	filename:	R-Star.cs                                               |
 |----------------------------------------------------------------------|
 |	version:	1.00                                                    |
 |	created:    19/09/2006	12:59                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Работа с файлами                                        |
 +----------------------------------------------------------------------+
 |  Отказоустойчивое кодирование по типу RAID систем                    |
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
            Console.Write(" Обработка томов: {0} %\r", Convert.ToInt32(progress));
        }

        static void OnFileSplittingFinish()
        {
            Console.WriteLine(" Обработка томов выполнена");
        }

        static void OnUpdateRSMatrixFormingProgress(double progress) 
        {
            Console.Write(" Расчет матрицы кодирования: {0} %\r", Convert.ToInt32(progress));
        }

        static void OnRSMatrixFormingFinish()
        {
            Console.WriteLine(" Расчет матрицы кодирования выполнен");
        }

        static void OnUpdateFileCodingProgress(double progress)
        {
            Console.Write(" Кодирование Рида-Соломона: {0} %\r", Convert.ToInt32(progress));
        }

        static void OnFileCodingFinish()
        {
            Console.WriteLine(" Кодирование Рида-Соломона выполнено");
        }

        static void OnUpdateFileAnalyzeProgress(double progress)
        {
            Console.Write(" Контроль целостности данных: {0} %\r", Convert.ToInt32(progress));
        }

        static void OnFileAnalyzeFinish()
        {
            Console.WriteLine(" Контроль целостности данных выполнен");
        }

        static void OnGetDamageStat(double percOfDamage, double percOfAltEcc)
        {
            if (
                    (percOfDamage == -1)
                 &&
                    (percOfAltEcc == -1)
                )
            {
                Console.WriteLine("     Ошибка: Все основные тома повреждены! Восстановление данных невозможно.");
                
                return;
            }

            if (
                    (percOfDamage == 0)
                 &&
                    (percOfAltEcc == -1)
                )
            {
                Console.WriteLine(" Многотомный архив не имеет повреждений в области основных томов!");
                Console.WriteLine("");

                return;
            }

            Console.WriteLine(" Всего поврежденных томов: {0} %, резерв томов для восстановления: {1} %", Convert.ToInt32(percOfDamage), Convert.ToInt32(percOfAltEcc));
            Console.WriteLine("");
        }

        /// <summary>
        /// Метод проверки строки на то, что она содержит только допустимые символы
        /// </summary>
        /// <returns>Является ли строка корректным Int-значением</returns>
        static bool IsIntString(String str)
        {            
            // Если длина строки больше 10 символов - выходим
            if (str.Length > 10)
            {
                return false;
            }

            // Пробегаем по всем символам строки...
            for (int i = 0; i < str.Length; i++)
            {
                //...и, если хотя бы один символ выходит за допустимые пределы
                // кодов для цифр - сообщаем об этом
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

            // Проверяем на корректность размера строки аргументов
            if (args.Length != 1)
            {
                Console.WriteLine(" Запуск: R-Star <имя файла>");

                return;
            }

            // Создаем экземпляр класса ядра RecoveryStar
            RecoveryStarCore eRecoveryStarCore = new RecoveryStarCore();

            // Создаем экземпляр класса для упаковки (распаковки) имени файла в префиксный формат
            FileNamer eFileNamer = new FileNamer();

            // Подписываемся на требуемых делегатов
            eRecoveryStarCore.OnUpdateFileSplittingProgress = new OnUpdateFileSplittingProgressHandler(OnUpdateFileSplittingProgress);
            eRecoveryStarCore.OnFileSplittingFinish = new OnFileSplittingFinishHandler(OnFileSplittingFinish);

            eRecoveryStarCore.OnUpdateRSMatrixFormingProgress = new OnUpdateRSMatrixFormingProgressHandler(OnUpdateRSMatrixFormingProgress);
            eRecoveryStarCore.OnRSMatrixFormingFinish = new OnRSMatrixFormingFinishHandler(OnRSMatrixFormingFinish);
            eRecoveryStarCore.OnUpdateFileCodingProgress = new OnUpdateFileCodingProgressHandler(OnUpdateFileCodingProgress);
            eRecoveryStarCore.OnFileCodingFinish = new OnFileCodingFinishHandler(OnFileCodingFinish);
            
            eRecoveryStarCore.OnUpdateFileAnalyzeProgress = new OnUpdateFileAnalyzeProgressHandler(OnUpdateFileAnalyzeProgress);
            eRecoveryStarCore.OnFileAnalyzeFinish = new OnFileAnalyzeFinishHandler(OnFileAnalyzeFinish);
            eRecoveryStarCore.OnGetDamageStat = new OnGetDamageStatHandler(OnGetDamageStat);

            // Имя файла для обработки
            String filename = eFileNamer.GetShortFilename(args[0]);

            // Путь к файлам для обработки
            String path = eFileNamer.GetPath(args[0]);

            // Количество основных томов
            int dataCount = 0;

            // Количество томов для восстановления
            int eccCount = 0;

            // Общее количество томов
            int allVolCount = 0;

            // Избыточность в процентах
            int redundancy = 0;

            // Строка, введенная пользователем
            String str;

            // Временное имя файла
            String tempFilename = filename;

            // Если имя файла может быть корректно распаковано из префиксного формата,
            // то файл принадлежит множеству файлов-томов
            if (eFileNamer.Unpack(ref tempFilename, ref dataCount, ref eccCount))
            {      
                Console.WriteLine(" Восстановление данных из отказоустойчивого набора томов...");
                Console.WriteLine("");

                // Запускаем восстановление данных
                eRecoveryStarCore.StartToRecover(path, filename, true);                

            } else
            {
                // Изначально предполагаем, что ввод некорректен
                bool inputOK = false;

                while (!inputOK)
                {
                    Console.Write(" Введите общее количество томов: ");

                    // Считываем строку данных
                    str = Console.ReadLine();

                    // Если пользователь ввел пустую строку - устанавливаем значение по-умолчанию
                    if (str == "")
                    {
                        str = "1024";
                        Console.WriteLine("     Установлено общее количество томов по-умолчанию: 1024");
                        Console.WriteLine("");

                    } else
                    {
                        // Если переданная строка оказалась некорректным значением "Int",
                        // переходим на очередную итерацию ввода
                        if (!IsIntString(str))
                        {                         
                            Console.WriteLine("     Ошибка: Введенная строка содержит недопустимые символы или слишком длинная!");
                            Console.WriteLine("");

                            continue;
                        }
                    }

                    // Если все нормально - устанавливаем значение
                    allVolCount = Convert.ToInt32(str);

                    // Производим проверку на допустимое количество томов
                    if (
                            (allVolCount >= 3)
                         &&
                            (allVolCount <= 16384)
                        )
                    {
                        inputOK = true;

                    } else
                    {
                        Console.WriteLine("     Ошибка: Общее количество томов должно находиться в диапазоне 3..16384!");
                        Console.WriteLine("");                        
                    }
                }

                // Изначально предполагаем, что ввод некорректен
                inputOK = false;

                while (!inputOK)
                {
                    Console.Write(" Введите избыточность кодирования в процентах: ");

                    // Считываем строку данных
                    str = Console.ReadLine();

                    // Если пользователь ввел пустую строку - устанавливаем значение по-умолчанию
                    if (str == "")
                    {
                        str = "50";
                        Console.WriteLine("     Установлена избыточность кодирования по-умолчанию: 50 %");
                    
                    } else
                    {
                        // Если переданная строка оказалась некорректным значением "Int",
                        // переходим на очередную итерацию ввода
                        if (!IsIntString(str))
                        {                            
                            Console.WriteLine("     Ошибка: Введенная строка содержит недопустимые символы или слишком длинная!");
                            Console.WriteLine("");

                            continue;
                        }
                    }

                    // Устанавливаем значение
                    redundancy = Convert.ToInt32(str);

                    // Проверяем установленное значение на корректность
                    if (
                            (redundancy > 0)
                         &&
                            (redundancy <= 100)
                        )
                    {
                        inputOK = true;

                    } else
                    {                        
                        Console.WriteLine("     Ошибка: Избыточность кодирования должна находиться в диапазоне 1..100 %!");
                        Console.WriteLine("");
                    }
                }
                
                // Абсолютное значение количества процентов на том
                double percByVol = (double)allVolCount / (double)200;

                // Вычисляем количество томов для восстановления
                eccCount = (int)((double)redundancy * percByVol); // Томов для восстановления
               
                // В случае необходимости корректируем количество томов для восстановления
                if (eccCount < 1)
                {
                    eccCount = 1;
                }
                
                // Количество основных томов находим по остаточному принципу
                dataCount = allVolCount - eccCount;

                // Пока количество томов
                while (eccCount >= dataCount)
                {
                    eccCount--;
                    dataCount++;
                }
                
                Console.WriteLine("");
                Console.WriteLine(" Приняты следующие параметры: ");
                Console.WriteLine(" Основных томов: {0}", dataCount);
                Console.WriteLine(" Томов для восстановления: {0}", eccCount);
                Console.WriteLine("");
                Console.WriteLine("===============================================================================");
                Console.WriteLine("");
                Console.WriteLine(" Создание отказоустойчивого набора томов...");
                Console.WriteLine("");

                // Запускаем отказоустойчивое кодирование
                eRecoveryStarCore.StartToProtect(path, filename, dataCount, eccCount, true);
            }

            // Ждем окончания обработки
            while (eRecoveryStarCore.InProcessing)
            {
                Thread.Sleep(1000);
            }

            // В связи с закрытием большого количества файловых потоков
            // необходимо дождаться записи изменений, внесенных потоком
            // кодирования в тело класса. Поток уже не работает, но
            // установленное им булевское свойство, возможно, ещё
            // "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 900; i++)
            {
                if (!eRecoveryStarCore.Finished)
                {
                    Thread.Sleep(1000);
                }
            }
                        
            Console.WriteLine("_______________________________________________________________________________");
            Console.WriteLine("");
            
            // Проверяем на корректность завершения обработки
            if (eRecoveryStarCore.ProcessedOK)
            {
                Console.WriteLine(" Обработка файлов завершена успешно!");

            } else
            {                
                Console.WriteLine(" Ошибка при работе с файлами!");
            }
        }
    }
}