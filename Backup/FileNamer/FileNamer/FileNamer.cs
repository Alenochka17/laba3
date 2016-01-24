/*----------------------------------------------------------------------+
 |	filename:	FileNamer.cs                                            |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	08:44                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Работа с именами файлов                                 |
 +----------------------------------------------------------------------+
 |  Упаковка (распаковка) имени файла в префиксный формат               |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;

namespace RecoveryStar
{
    /// <summary>
    /// Класс для формирования имени тома на основе порядкового номера и конфигурации кодера
    /// </summary>
    public class FileNamer
    {
        #region Construction & Destruction

        public FileNamer()
        {
            // Устанавливаем максимальную длину имени файла на целевом носителе,
            // равную 64 символам
            _maxFilenameLength = 64;

            // Инициализируем массив преобразования значения в "HEX-формат"
            _HEX = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// Возвращает путь, выделяя его из полного имени файла
        /// </summary>
        /// <param name="fullFilename">Полное имя файла</param>
        /// <returns>Путь</returns>
        public String GetPath(String fullFilename)
        {
            int lastIdxOfSeparator = -1;

            for (int i = 0; i < fullFilename.Length; i++)
            {
                if (fullFilename[i] == '\\')
                {
                    lastIdxOfSeparator = i;
                }
            }

            if (lastIdxOfSeparator != -1)
            {
                return fullFilename.Substring(0, (lastIdxOfSeparator + 1));

            } else
            {
                return "";
            }
        }

        /// <summary>
        /// Возвращает короткое имя файла, отсекая путь
        /// </summary>
        /// <param name="fullFilename">Полное имя файла</param>
        /// <returns>Короткое имя файла</returns>
        public String GetShortFilename(String fullFilename)
        {            
            int lastIdxOfSeparator = -1;

            for (int i = 0; i < fullFilename.Length; i++)
            {
                if (fullFilename[i] == '\\')
                {
                    lastIdxOfSeparator = i;
                }
            }

            if (lastIdxOfSeparator != -1)
            {
                return fullFilename.Substring(lastIdxOfSeparator + 1);

            } else
            {
                return fullFilename;
            }
        }

        /// <summary>
        /// "Упаковка" исходного имени файла в префиксный формат
        /// </summary>
        /// <param name="filename">Имя файла для "упаковки"</param>
        /// <param name="volNum">Номер текущего тома</param>
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param>  
        /// <returns>Булевский флаг операции</returns>
        public bool Pack(ref String filename, int volNum, int dataCount, int eccCount)
        {
            // Проверяем длину входного имени на возможность упаковки без
            // нарушения ограничений его максимальной выходной длины
            if ((filename.Length + _prefixLength) > _maxFilenameLength)
            {
                return false;
            }

            // Формируем шаблон префикса, который будет редактироваться на основе
            // представленных данных
            Char[] prefixChr = new Char[] { '@', 'V', 'V', 'V', 'V', 'N', 'N', 'N', 'N', 'M', 'M', 'M', 'M', '.' };

            // Пишем данные номера тома
            if (!Int16ToHEX(volNum, ref prefixChr, 1))
            {
                return false;
            }

            // Пишем данные количества основных томов в конфигурации кодера
            if (!Int16ToHEX(dataCount, ref prefixChr, 5))
            {
                return false;
            }

            // Пишем данные количества томов для восстановления в конфигурации кодера
            if (!Int16ToHEX(eccCount, ref prefixChr, 9))
            {
                return false;
            }

            // Преобразуем массив символов префикса в строку и присоединяем к ней
            // исходное имя файла
            filename = new String(prefixChr) + filename;

            return true;
        }

        /// <summary>
        /// "Распаковка" имени файла из префиксного формата в исходный
        /// </summary>
        /// <param name="filename">Имя файла для "распаковки"</param>
        /// <param name="volNum">Номер текущего тома</param>
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param>
        /// <returns>Булевский флаг операции</returns>
        public bool Unpack(ref String filename, ref int volNum, ref int dataCount, ref int eccCount)
        {            
            // Если имя файла для распаковки содержит менее одного символа
            // за исключением префикса, или если упакованное имя файла не
            // содержит специфического символа в начале и точки в конце,
            // то отказываем в обработке
            if (
                    (filename.Length < 15)
                 ||
                    (filename[0] != '@')
                 ||
                    (filename[13] != '.')
                )
            {
                return false;
            }

            // Инициализируем массив для хранения набора символов "HEX-представления"
            Char[] prefixPartChr = new Char[12];

            // Копируем данные "HEX-представления" volNum, dataCount, eccCount в массив Char
            filename.CopyTo(1, prefixPartChr, 0, 12);

            // Извлекаем номер тома            
            if (!HEXToInt16(ref volNum, prefixPartChr, 0))
            {
                return false;
            }

            // Извлекаем количество основных томов            
            if (!HEXToInt16(ref dataCount, prefixPartChr, 4))
            {
                return false;
            }

            // Извлекаем количество томов для восстановления
            if (!HEXToInt16(ref eccCount, prefixPartChr, 8))
            {
                return false;
            }

            // Убираем префикс из имени файла
            filename = filename.Substring(14, (filename.Length - 14));

            return true;
        }
        
        /// <summary>
        /// "Распаковка" имени файла из префиксного формата в исходный
        /// </summary>
        /// <param name="filename">Имя файла для "распаковки"</param>        
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param>
        /// <returns>Булевский флаг операции</returns>
        public bool Unpack(ref String filename, ref int dataCount, ref int eccCount)
        {
            int volNum = 0;

            return Unpack(ref filename, ref volNum, ref dataCount, ref eccCount);
        }

        /// <summary>
        /// "Распаковка" имени файла из префиксного формата в исходный
        /// </summary>
        /// <param name="filename">Имя файла для "распаковки"</param>        
        /// <returns>Булевский флаг операции</returns>
        public bool Unpack(ref String filename)
        {
            int volNum    = 0;
            int dataCount = 0;
            int eccCount  = 0;

            return Unpack(ref filename, ref volNum, ref dataCount, ref eccCount);
        }

        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// Преобразование значения int в четыре символа "HEX-представления"
        /// </summary>
        /// <param name="value">Исходное значение</param>
        /// <param name="target">Целевой массив для вывода</param>
        /// <param name="startOutPos">Стартовая позиция в массиве для преобразования</param>
        private bool Int16ToHEX(int value, ref Char[] target, int startOutPos)
        {
            // Если указываем такую позицию для вывода текстового представления,
            // которая выходит за допустимые пределы целевого массива, сообщаем
            // об этом
            if ((startOutPos + 3) > target.GetUpperBound(0))
            {
                return false;
            }

            // Преобразуем данные переменной в четыре текстовых представления "HEX"
            target[startOutPos + 3] = _HEX[((value & 0x0000000F) >> 0)];
            target[startOutPos + 2] = _HEX[((value & 0x000000F0) >> 4)];
            target[startOutPos + 1] = _HEX[((value & 0x00000F00) >> 8)];
            target[startOutPos + 0] = _HEX[((value & 0x0000F000) >> 12)];

            return true;
        }

        /// <summary>
        /// Преобразование четырех символов "HEX-представления" в int16
        /// </summary>
        /// <param name="value">Возвращаемое значение</param>
        /// <param name="source">Исходный массив "HEX-данных"</param>
        /// <param name="startOutPos">Стартовая позиция во входном массиве для преобразования</param>
        private bool HEXToInt16(ref int value, Char[] source, int startInPos)
        {
            // Если указываем такую позицию для вывода текстового представления,
            // которая выходит за допустимые пределы целевого массива, сообщаем
            // об этом
            if ((startInPos + 3) > source.GetUpperBound(0))
            {
                return false;
            }

            // Изначально обнуляем возвращаемое значение
            value = 0;

            // Преобразуем четыре текстовых представления "HEX" в данные переменной "int"
            for (int i = startInPos, shift = 12; i < (startInPos + 4); i++, shift -= 4)
            {
                // Изначально полагаем, что символ не найден в словаре "_HEX"
                bool letterSearchRes = false;

                // Индексный эквивалент символа в словаре
                int letterIdx = 0;

                // Пробегаемся по всему словарю "_HEX"
                for (int j = 0; j < 16; j++)
                {
                    // Если текущий символ совпал с имеющимся в словаре
                    if (source[i] == _HEX[j])
                    {
                        letterSearchRes = true;
                        letterIdx = j;

                        break;
                    }
                }

                // Если хотя бы один символ не был найдерн в словаре,
                // сообщаем что произошла ошибка декодирования параметров
                if (letterSearchRes != true)
                {
                    return false;
                }

                // Если дошли до данного участка кода, можно преобразовывать
                // текущий индекс к требуемому разряду целевой переменной int16
                value += (letterIdx << shift);
            }

            return true;
        }

        #endregion Private Operations

        #region Public Properties

        /// <summary>
        /// Максимальная длина имени файла на целевом носителе
        /// </summary>
        public int MaxFilenameLength
        {
            get
            {
                return _maxFilenameLength;
            }

            set
            {
                // Если устанавливаемое значение больше длины префикса, позволяем
                // записать значение (т.к. имя должно содержать хотя бы один символ)
                if (value > 14)
                {
                    _maxFilenameLength = value;
                }
            }
        }

        #endregion Public Properties

        #region Constants

        // Длина префикса, добавляемого к имени при "упаковке"
        private const int _prefixLength = 14; // "@VVVVNNNNMMMM."

        #endregion Constants

        #region Data

        // Максимальная длина имени файла на целевом носителе
        private int _maxFilenameLength;

        // Строка символов шестнадцатеричного представления
        char[] _HEX;

        #endregion Data

    } // class FileNamer
}
