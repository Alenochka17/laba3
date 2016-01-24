/*----------------------------------------------------------------------+
 |	filename:	CRC-64.cs                                               |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   01/03/2007	11:52                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Контроль целостности данных                             |
 +----------------------------------------------------------------------+
 |  Реализация функциональности контроля целостности файла (CRC-64)     |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{
    /// <summary>
    /// Делегат обновления прогресса контроля целостности файла
    /// </summary>
    /// <param name="progress">Текущий прогресс</param>
    public delegate void OnUpdateFileIntegrityCheckProgressHandler(double progress);

    /// <summary>
    /// Делегат завершения процесса контроля целостности файла
    /// </summary>
    public delegate void OnFileIntegrityCheckFinishHandler();

    /// <summary>
    /// Класс расчета CRC-64
    /// </summary>
    public class CRC64
    {
        #region Construction & Destruction

        /// <summary>
        /// Конструктор класса расчета CRC-64
        /// </summary>
        public CRC64()
        {
            // Инициализируем таблицу расчета CRC-64
            CRC64Init();
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// Вычисление CRC-64 с переданным начальным значением
        /// </summary>
        /// <param name="CRC64">Переданное начальное значение CRC-64</param>
        /// <param name="source">Массив исходных данных</param>
        /// <param name="offset">Смещение в массиве исходных данных</param>
        /// <param name="length">Длина участка для вычисления CRC-64</param>
        /// <returns>Вычисленное значение CRC-64</returns>
        public UInt64 Process(UInt64 CRC64, byte[] source, int offset, int length)
        {
            for (int i = offset; i < (offset + length); i++)
            {
                CRC64 = ((CRC64 >> 8) & 0x00FFFFFFFFFFFFFF) ^ _CRC64Table[(0x00000000000000FF & CRC64) ^ source[i]];
            }

            // Выполняем такое количество дополнительных итераций для выравнивания входного
            // сообщения, чтобы его размер соответствовал кратности 64 бита    
            for (int i = 0; i < (length % 8); i++)
            {
                CRC64 = ((CRC64 >> 8) & 0x00FFFFFFFFFFFFFF) ^ _CRC64Table[0x00000000000000FF & CRC64];
            }
           
            return CRC64;
        }

        /// <summary>
        /// Вычисление CRC-64
        /// </summary>        
        /// <param name="source">Массив исходных данных</param>
        /// <param name="offset">Смещение в массиве исходных данных</param>
        /// <param name="length">Длина участка для вычисления CRC-64</param>
        /// <returns>Вычисленное значение CRC-64</returns>
        public UInt64 Process(byte[] source, int offset, int length)
        {
            return Process(0xFFFFFFFFFFFFFFFF, source, offset, length);
        }

        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// Инициализация таблицы расчета CRC-64
        /// </summary>
        private void CRC64Init()
        {
            // Выделяем память под таблицу расчета CRC-64
            _CRC64Table = new UInt64[_CRC64TableSize];

            // Заполнение таблицы CRC-64
            for (int i = 0; i < _CRC64TableSize; i++)
            {
                UInt64 c = (UInt64)i;

                for (int j = 0; j < 8; j++)
                {
                    if ((c & 1) != 0)
                    {
                        c = (c >> 1) ^ _CRC64GenPoly;

                    } else
                    {
                        c = (c >> 1);
                    }
                }

                // Пишем рассчитанное значение в массив
                _CRC64Table[i] = c;
            }
        }

        #endregion Private Operations

        #region Constants

        // Размер таблицы для расчета CRC-64
        private const int _CRC64TableSize = 256;

        // Генераторный полином для CRC-64
        private const UInt64 _CRC64GenPoly = 0xC96C5795D7870F42;                                              

        #endregion Constants

        #region Data

        // Таблица для расчета CRC-64
        private UInt64[] _CRC64Table;

        #endregion Data

    } // class CRC64

    /// <summary>
    /// Класс вычисления и проверки целостности файла на основе CRC-64
    /// </summary>
    public class FileIntegrityCheck
    {
        #region Construction & Destruction

        /// <summary>
        /// Конструктор класса проверки целостности файлов
        /// </summary>
        public FileIntegrityCheck()
        {
            // Создаем экземпляр класса расчета CRC-64
            _eCRC64 = new CRC64();
            
            // Инициализируем имя файла по-умолчанию            
            _fullFilename = "NONAME";

            // Размер файлового буфера (по-умолчанию) - 64 Мб
            _bufferLength = (64 * 1024 * 1024);
          
            // Выделяем память под файловый буфер
            _buffer = new byte[_bufferLength];

            // В переменной Int64 содержится 8 байт
            _crc64Arr = new byte[8];

            // Экземляр класса полностью закончил обработку?
            _finished = true;

            // Обработка произведена корректно?
            _processedOK = false;

            // По-умолчанию устанавливается фоновый приоритет
            _threadPriority = 0;

            // Инициализируем событие прекращения обработки файла
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };
            
            // Инициализируем cобытие продолжения обработки файла
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Событие, устанавливаемое по завершении обработки
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Delegates

        /// <summary>
        /// Делегат обновления прогресса контроля целостности файла
        /// </summary>        
        public OnUpdateFileIntegrityCheckProgressHandler OnUpdateFileIntegrityCheckProgress;

        /// <summary>
        /// Делегат завершения процесса контроля целостности файла
        /// </summary>
        public OnFileIntegrityCheckFinishHandler OnFileIntegrityCheckFinish;

        #endregion Delegates

        #region Public Operations

        /// <summary>
        /// Метод запуска потока обработки вычисления и записи CRC64 в конец файла
        /// </summary>
        /// <param name="fullFilename">Имя файла для обработки</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToWriteCRC64(String fullFilename, bool runAsSeparateThread)
        {
            // Если поток вычисления CRC-64 работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }

            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;
                        
            if (fullFilename == null)
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                return false;
            }

            // Если исходный файл не существует, сообщаем об ошибке
            if (!File.Exists(fullFilename))
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                return false;            
            }
                        
            // Сохраняем имя файла
            _fullFilename = fullFilename;

            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Запускаем вычисление и запись CRC-64 в конец файла
                WriteCRC64();
                
                // Возвращаем результат обработки
                return _processedOK;
            }
                        
            // Создаем поток вычисления и записи CRC-64...
            _thrFileIntegrityCheck = new Thread(new ThreadStart(WriteCRC64));

            //...затем даем ему имя...
            _thrFileIntegrityCheck.Name = "FileIntegrityCheck.WriteCRC64()";
                            
            //...устанавливаем выбранный приоритет задачи...
            _thrFileIntegrityCheck.Priority = _threadPriority;
                     
            //...и запускаем его
            _thrFileIntegrityCheck.Start();
  
            // Сообщаем, что все нормально
            return true;
        }

        /// <summary>
        /// Метод запуска потока обработки проверки CRC64, записанного в конец файла
        /// </summary>
        /// <param name="fullFilename">Имя файла для обработки</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToCheckCRC64(String fullFilename, bool runAsSeparateThread)
        {
            // Если поток вычисления CRC-64 работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }

            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;
                        
            if (fullFilename == null)
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Если исходный файл не существует, сообщаем об ошибке
            if (!File.Exists(fullFilename))
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Сохраняем имя файла
            _fullFilename = fullFilename;

            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Запускаем вычисление и проверку значения CRC-64
                CheckCRC64();

                // Возвращаем результат обработки
                return _processedOK;
            }
            
            // Создаем поток вычисления и проверки CRC-64...
            _thrFileIntegrityCheck = new Thread(new ThreadStart(CheckCRC64));

            //...затем даем ему имя...
            _thrFileIntegrityCheck.Name = "FileIntegrityCheck.CheckCRC64()";
                
            //...устанавливаем выбранный приоритет задачи...
            _thrFileIntegrityCheck.Priority = _threadPriority;
                        
            //...и запускаем его
            _thrFileIntegrityCheck.Start();

            // Сообщаем, что все нормально
            return true;
        }

        /// <summary>
        /// Метод остановки потока
        /// </summary>
        public void Stop()
        {
            // Указываем, что поток обработки больше не должен выполняться
            _executeEvent[0].Set(); // Это необходимо для принудительного снятия с паузы
            _exitEvent[0].Set();
        }

        /// <summary>
        /// Постановка потока обработки на паузу
        /// </summary>
        public void Pause()
        {
            // Ставим обработку на паузу
            _executeEvent[0].Reset();            
        }

        /// <summary>
        /// Снятие потока обработки с паузы
        /// </summary>
        public void Continue()
        {
            // Снимаем обработку c паузы
            _executeEvent[0].Set();            
        }
        
        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// Вычисление CRC-64 указанного файла
        /// </summary>
        /// <param name="fullFilename">Имя файла для обработки</param>
        /// <param name="endOffset">Смещение, "недорабатываемое" при вычислениях с конца файла</param>        
        /// <returns>Булевский флаг операции</returns>
        private bool CalcCRC64(String fullFilename, int endOffset)
        {
            // Экземпляр файлового потока
            FileStream eFileStream = null;

            try
            {                
                // Открываем файловый поток на чтение
                eFileStream = new FileStream(fullFilename, FileMode.Open, System.IO.FileAccess.Read);
                
                // Узнаем длину файла для обработки
                Int64 fileLength = eFileStream.Length;

                // Если размер файла меньше смещения - это ошибка
                if ((fileLength - endOffset) == 0)
                {
                    return false;
                }
                
                // Узнаем количество основных итераций по вычислению CRC-64
                Int64 nIterations = ((fileLength - endOffset) / _bufferLength);
                
                // Вычисляем остаток, не подпадающий под вычисления основных итераций
                int iterRest = (int)((fileLength - endOffset) - (nIterations * _bufferLength));

                // Вычисляем значение модуля, который позволит выводить процент обработки
                // ровно при единичном приращении для цикла по "i"
                int progressMod1 = (int)(nIterations / 100);

                // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
                // прогресс выводился на каждой итерации (файл очень маленький)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }
            
                // Работа с полноразмерными буферами (работа в основных итерациях)
                for (Int64 i = 0; i < nIterations; i++)
                {
                    // Читаем данные в буфер
                    eFileStream.Read(_buffer, 0, _bufferLength);

                    // Обновляем значение CRC-64 на основании только что считанного фрагмента файла
                    _crc64 = _eCRC64.Process(_crc64, _buffer, 0, _bufferLength);

                    // Выводим прогресс обработки                  
                    if (
                            ((i % progressMod1) == 0)
                         &&
                            (OnUpdateFileIntegrityCheckProgress != null)
                        )
                    {
                        OnUpdateFileIntegrityCheckProgress(((double)(i + 1) / (double)nIterations) * 100.0);
                    }

                    // В случае, если требуется постановка на паузу, событие "_executeEvent"
                    // будет сброшено, и будем на паузе вплоть до его появления
                    ManualResetEvent.WaitAll(_executeEvent);

                    // Если указано, что требуется выйти из потока - выходим
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        if (eFileStream != null)
                        {
                            eFileStream.Close();                            
                            eFileStream = null;
                        }

                        return false;
                    }                    
                }

                // Дорабатываем остаток (если он есть)
                if (iterRest > 0)
                {
                    // Читаем данные в буфер
                    eFileStream.Read(_buffer, 0, iterRest);

                    // Обновляем значение CRC-64 на основании только что считанного фрагмента файла
                    _crc64 = _eCRC64.Process(_crc64, _buffer, 0, iterRest);                   
                }

                // Сообщаем, что обработка файла закончена
                if (OnFileIntegrityCheckFinish != null)
                {
                    OnFileIntegrityCheckFinish();
                }
            }

            // Если было хотя бы одно исключение - закрываем файловый поток и
            // сообщаем об ошибке
            catch (IOException e)
            {
                if (eFileStream != null)
                {
                    eFileStream.Close();
                    eFileStream = null;
                }

                return false;                               
            }

            // Если дошли до данного участка кода - исключений не было - сообщаем,
            // что все корректно
            if (eFileStream != null)
            {
                eFileStream.Close();
                eFileStream = null;
            }

            return true;
        }

        /// <summary>
        /// Вычисление и запись в конец файла значения CRC-64
        /// </summary>                
        private void WriteCRC64()
        {
            // Начальное значение CRC-64
            _crc64 = 0xFFFFFFFFFFFFFFFF;

            // Если вычисление CRC-64 с данного файла прошло корректно...
            if (CalcCRC64(_fullFilename, 0))
            {
                // Экземпляр файлового потока
                FileStream eFileStream = null;

                // Экземпляр класса записи обычных типов данных в двоичный файл
                BinaryWriter eBinaryWriter = null;

                try
                {   
                    //...открываем файловый поток на запись...
                    eFileStream = new FileStream(_fullFilename, FileMode.Append, System.IO.FileAccess.Write);

                    //...и используем его для инициализации экземпляра класса записи
                    // обычных типов данных в двоичный файл
                    eBinaryWriter = new BinaryWriter(eFileStream);

                    // Перемещаемся на конец файла...
                    eBinaryWriter.Seek(0, SeekOrigin.End);

                    //...и пишем в его конец вычисленное значение CRC-64,...
                    eBinaryWriter.Write(_crc64);

                    //...сливаем файловый буфер...
                    eBinaryWriter.Flush();

                    //...и закрываем файл
                    if (eBinaryWriter != null)
                    {
                        eBinaryWriter.Close();
                        eBinaryWriter = null;
                    }                    
                }

                // Если было хотя бы одно исключение - закрываем файловый поток и
                // сообщаем об ошибке
                catch (IOException e)
                {
                    // Закрываем файл
                    if (eBinaryWriter != null)
                    {
                        eBinaryWriter.Close();
                        eBinaryWriter = null;
                    }

                    // Сбрасываем флаг корректности результата
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }

                // Указываем на то, что CRC-64 у данного файла было вычислено корректно
                _processedOK = true;

            } else
            {
                // Указываем на то, что CRC-64 у данного файла было вычислено некорректно
                _processedOK = false;
            }

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// Вычисление и проверка значения CRC-64, записанного в конце файла
        /// </summary>        
        private void CheckCRC64()
        {
            // Начальное значение CRC-64
            _crc64 = 0xFFFFFFFFFFFFFFFF;

            // Сохраненное в файле значение CRC-64:
            UInt64 crc64;

            // Экземпляр файлового потока
            FileStream eFileStream = null;

            // Если вычисление CRC-64 с данного файла со смещением "8" прошло корректно...           
            if (CalcCRC64(_fullFilename, 8))
            {                
                try
                {
                    //...то открываем файловый поток на чтение...
                    eFileStream = new FileStream(_fullFilename, FileMode.Open, System.IO.FileAccess.Read);

                    //...и выполняем позиционирование на конец файла, чтобы считать значение CRC-64
                    eFileStream.Seek(((Int64)eFileStream.Length - 8), SeekOrigin.Begin);

                    // Читаем сохраненное в конце файла значение CRC-64...
                    eFileStream.Read(_crc64Arr, 0, 8);

                    //...и закрываем файл
                    if (eFileStream != null)
                    {
                        eFileStream.Close();
                        eFileStream = null;
                    }                    
                }

                // Если было хотя бы одно исключение - закрываем файловый поток и
                // сообщаем об ошибке
                catch (IOException e)
                {
                    if (eFileStream != null)
                    {
                        eFileStream.Close();
                        eFileStream = null;
                    }

                    // Сбрасываем флаг корректности результата
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }

                // Теперь преобразуем массив byte[] в Int64
                crc64  = 0x0000000000000000;              
                crc64 |= ((UInt64)_crc64Arr[0] << 0)  & (0x00000000000000FF);
                crc64 |= ((UInt64)_crc64Arr[1] << 8)  & (0x000000000000FF00);
                crc64 |= ((UInt64)_crc64Arr[2] << 16) & (0x0000000000FF0000);
                crc64 |= ((UInt64)_crc64Arr[3] << 24) & (0x00000000FF000000);
                crc64 |= ((UInt64)_crc64Arr[4] << 32) & (0x000000FF00000000);
                crc64 |= ((UInt64)_crc64Arr[5] << 40) & (0x0000FF0000000000);
                crc64 |= ((UInt64)_crc64Arr[6] << 48) & (0x00FF000000000000);
                crc64 |= ((UInt64)_crc64Arr[7] << 56) & (0xFF00000000000000);
         
                // Если вычисленное значение CRC-64 не совпало с сохраненным,
                // указываем на ошибку
                if (_crc64 != crc64)
                {
                    _processedOK = false;

                } else
                {
                    _processedOK = true;
                }
                                               
            } else
            {
                // Указываем на то, что чтение CRC-64 файла прошло не корректно
                _processedOK = false;
            }

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        #endregion Private Operations

        #region Public Properties

        /// <summary>
        /// Булевское свойство "Файл обрабатывается?"
        /// </summary>
        public bool InProcessing
        {
            get
            {
                if (
                        (_thrFileIntegrityCheck != null)
                     &&
                        (
                                (_thrFileIntegrityCheck.ThreadState == ThreadState.Running)
                            ||
                                (_thrFileIntegrityCheck.ThreadState == ThreadState.WaitSleepJoin)
                        )                        
                    )
                {
                    return true;

                } else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Булевское свойство "Экземпляр класса закончил обработку
        /// (имеет актуальное состояние переменных-членов)?"
        /// </summary>   
        public bool Finished
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение
                if (!InProcessing)
                {
                    return _finished;

                } else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Булевское свойство "CRC-64 файла вычислено корректно?"
        /// </summary>
        public bool ProcessedOK
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение
                if (!InProcessing)
                {
                    return _processedOK;
                    
                } else
                {
                    return false;
                }
            }
        }
        
        /// <summary>
        /// Имя файла, обработанного последним
        /// </summary>
        public String fullFilename
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение...
                if (!InProcessing)
                {
                    return _fullFilename;

                } else
                {
                    //...а иначе сообщаем об обратном
                    return "-1";
                }               
            }           
        }

        /// <summary>
        /// Имя файла, обработанного последним
        /// </summary>
        public UInt64 CRC64
        {
            get
            {
                // Если класс не занят обработкой - возвращаем реальное значение...
                if (!InProcessing)
                {
                    return _crc64;

                } else
                {
                    ///...а иначе сообщаем об обратном
                    return 0xFFFFFFFFFFFFFFFF;
                }              
            }            
        }
                
        /// <summary>
        /// Размер Файлового буфера
        /// </summary>
        public int BufferLength
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение...
                if (!InProcessing)
                {
                    return _bufferLength;

                } else
                {
                    //...а иначе сообщаем об обратном
                    return -1;
                }
            }

            set
            {
                // Если класс не занят обработкой - устанавливаем значение...
                if (!InProcessing)
                {
                    //... но только если оно не нарушает минимальный размер буфера - 8 Кб
                    if (value >= (8 * 1024))
                    {
                        _bufferLength = value;
                    }
                }
            }
        }

        /// <summary>
        /// Приоритет процесса
        /// </summary>
        public int ThreadPriority
        {
            get
            {
                return (int)_threadPriority;
            }

            set
            {
                if (
                        (_thrFileIntegrityCheck != null)
                     &&
                        (_thrFileIntegrityCheck.IsAlive)
                    )
                {
                    switch (value)
                    {
                        default:
                        case 0:
                        {
                            _threadPriority = System.Threading.ThreadPriority.Lowest;

                            break;
                        }

                        case 1:
                        {
                            _threadPriority = System.Threading.ThreadPriority.BelowNormal;

                            break;
                        }

                        case 2:
                        {
                            _threadPriority = System.Threading.ThreadPriority.Normal;

                            break;
                        }

                        case 3:
                        {
                            _threadPriority = System.Threading.ThreadPriority.AboveNormal;

                            break;
                        }

                        case 4:
                        {
                            _threadPriority = System.Threading.ThreadPriority.Highest;

                            break;
                        }
                    }
                    
                    // Устанавливаем выбранный приоритет процесса
                    _thrFileIntegrityCheck.Priority = _threadPriority;                                    
                }                
            }
        }

        /// <summary>
        /// Событие, устанавливаемое по завершении обработки
        /// </summary>
        public ManualResetEvent[] FinishedEvent
        {
            get
            {
                return _finishedEvent;
            }
        }

        #endregion Public Properties
      
        #region Data

        // Экземпляр класса расчета CRC-64
        private CRC64 _eCRC64;

        // Имя файла для обработки
        private String _fullFilename;

        // Файловый буфер        
        private byte[] _buffer;

        // Размер файлового буфера (64 Мб)
        private int _bufferLength;

        // Буфер для преобразования байтового представления CRC-64
        private byte[] _crc64Arr;

        // Значение CRC-64, соответствующее "_fullFilename"
        private UInt64 _crc64;

        // Экземляр класса полностью закончил обработку?
        private bool _finished;

        // CRC-64 вычислено корректно?
        private bool _processedOK;
        
        // Поток вычисления CRC-64 файла "_fullFilename" с сохранением результата в "_processedOK"
        private Thread _thrFileIntegrityCheck;

        // Приоритет процесса расчета CRC-64
        private ThreadPriority _threadPriority;

        // Событие прекращения обработки файла
        private ManualResetEvent[] _exitEvent;

        // Событие продолжения обработки файла
        private ManualResetEvent[] _executeEvent;

        // Событие, устанавливаемое по завершении обработки
        private ManualResetEvent[] _finishedEvent;
     
        #endregion Data

    } // class FileIntegrityCheck
}
