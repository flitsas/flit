namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Trazos sintéticos (PNG) para el simulador FUR. No son firmas de personas reales.
/// </summary>
internal static class FurPreviewSignatures
{
    internal static byte[] Vendedor { get; } = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAaQAAABuCAYAAABoSGdTAAAGY0lEQVR4nO3dW27bOhAA0PTiLoH7X6H30CIfRl1HsijxIZJzDlAETZtYpoccDqnH1xcAAAAA"
            + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAC/t19wEArCil9Hvr+4/Hw7i747+9f4CRO/leZ4cRfIpPsbtPpmYKZpvMIjfhqJR+UiEx/WzTjJNR"
            + "bMXid+KRfPKokBjamWSj0zNaIjr6v2L2XyokpvJptqlS4g5n4+41fsXsvyQkpvHakS2DMLIzlY+k9Jclu8YBoyS/7szShmUQRhgbcvv71Z9bnUa4YbYi+I5d"
            + "STCSEj2VJhXx+pMlu5cztXqVzr1fbzY12kXbMjp7ST/9/xVUr7O3jl7HLKld28NMcZpS+h093kO9+Zwk1CMgRjmOEZUmaAmeHmrFmb2kf4UY9I4SwJ2D/8jH"
            + "9j5ze//a4nVe/371NSQlWqodX+L1r9sHu9ZmuuXMSMfau4o7c2Hhmd814ufMvFpVNGJ28T2kkQb3XM9jez/2599bHvuVkwBadqKRPydicqJMe0ueZTf7/aT2"
            + "jrVFhzhztt+n9is5ttqzTmcvsRVbJXeKr1nBs2+pBl01aGq8r9f9nzM/dzYxll6hbgmE0e643WtcSZaa10lIqyaj3PdXq3oqubjvzO/osaTaY6mTcZT2ga0T"
            + "dnqOK0lCWiMhRTp1cuu91khGta+1urK8V/tz08HXd7Xyv6r12JKCT6Km30OKlIy23l/pffZq7K3tLettHVvP/b3VY2EmW/s3pU//Pfr55+f/+rUk1nrGUwp6"
            + "F5epO2y0ZFRyp4keV4Ff6UQ9jylSfIzk6uDaq8rOqbJ6xU4KHq/TvmHJ6KcRAni0B+pF7+AjcMPi6+31CBazUy7ZSUZ57XKH3CWRXh3NKeDrizZor2z6C2Mj"
            + "BePe/ssIiSj3It9InxfljxE5Ip7WMt3gELWcPXPad6R2yaFt5m33vfsorn5n7BS0P0+1ZDdiJXCXVndNWJFlu/5qXxe39zWCFKg/T5WQXkUKyJz3Hrk9gDVM"
            + "m5Aiab0OH4GEre1n8gg6wQz5pldORlHXnnNoG+08mxSsP6uQBlZa5aiSuFOEAZS6JKTFGATy2kaybkO71vUIltQlpElEC0zmJl7rSwH2hSWkQUVbO+5FW7YT"
            + "YcCkLQlpQZam8hhA28cf2vMMCWlwOnfbNpWU6tCOfaTFq1AJaUCrB91otHc9JlDtpYXHBwkJKLLyADmKR5Bl0OoJae9JoeS3X7QgvIO2ZTaPBkvNz/F6lHG7"
            + "WYU0wpuDT+wllTOBmm8ilT4koLvHbUt2hKZSYlbpZPLIrYLuTErVE9LMs867y1ezzXvNFq/E9Lgwxl5JXl83aLZHMdPg+qnxex57zTabqf1HoL202UzSzpOY"
            + "t76/N769jwsjjINdEtLIA2POTKDXcT+PRTLqb5Z4fRrh0fCS+H1SQQVT8nDP1nHWNSGN1MmvfKAtj111dK+j2eHrI7NzY6d2vIw0eao5gaKs/XPVepZay8+7"
            + "aSCNUAJeTZa9E2rNzm3mWt5uNdX+THu83hExNo7UcKLyaRx8naTV0nUpak+rN3fmWLZeu1dSalUd1fh9EbVITCWfw2jV/DfV0VhS4z3jXsXFLRv2OXo16tmn"
            + "sLY4NtXRmJ6TpLMTkxoz1rMDQM89MNVRXKnxWHjrRugZtSuHs7+z1QfRonObvcastHolJQmJ1GhFa7h9nF7roFd+V4ukpHOvrWZSulLN5/5cLvFKS79W36Cr"
            + "3UFrJiWdO54eS9ctk5KYJdStg54dJ2dp4lPn3rvbQmnHPHMx2SfuChDTd/xsxfjr917/lL5Gq3hzsgwhKqTSqqnH2SA1qiRnw9Fa7RhTHRGuQtqSM2M8uv9c"
            + "zRld7dmn2SYtiCtmM0WFtOWuq+U/HcOVK6ENGrRWI97ELD1MUSFtOaqYStbgr7IvxIhmvgM/sUybkJ7eN4h7JqLSvSPVEXeQlBjV9Akp98y81q/9dHTmX5eD"
            + "ggNnYtEkil6WSEh3y0lKkhF3OnOvxrP/B2qRkBprdT0U9EpK0ItBcbEz/+BIzgTJJIo7qJAqykk0khF3u3KncXFLD2bqDejQzEBFz2gkJAjszsdVwztLdhDY"
            + "0cXlfY8GgPBy7qAPAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADA16T+AI5TXGZVLIFTAAAAAElFTkSuQmCC");

    internal static byte[] Comprador { get; } = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAaQAAABuCAYAAABoSGdTAAAF6klEQVR4nO3dWY7jNhAA0E6QI+j+J/QdMmggDgxDskWJSxX5HjAfM9O2KKmKRVJL//wAAAAA"
            + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADQx1+dtgPQ1LZt/77+/fF46N+S+Xt0A8iT6O8JD5FjU7zmYwTBoaOENvIkgrMFR7zmoSDx"
            + "PwnODIOlvf9TlHJQkE64M/XPkgil+5hlv5jPmYKjKOWkUxmw7hytM/+2z8/2umhMtiVkMZvLP6MbEEHvi5+v2xtdnEpGkkfLIdCSa5nrCDVS76mkY71TNHpt"
            + "56orI8hIBZV5fcqdkrgTr3ks1ZmcKQ49Otio7ZDkzFaM9r7PICquJQrS2WskI4K6Vtvubrd0O9bmyVCI9r5bQYpr+oJUa/25xy3RvdbKa9yBpCBxN9Z6P2Zg"
            + "lhTftAWpdufe81pQy0SteTusBKc01kq1GoyZJcU0ZUGq/QzClcTqUfg+ba/X6FNRiinKUvBVPdqnKMUzVUFq8TBcSQDf3X7vW6prJKQEj2fUnZ3frv9EuX3b"
            + "LCmuaZ5Dal2ManzHa3tKO43axcrocE6lcfL787UHJu+e3x8t5mrtO/VMcTJaXWC/MvpvvW5+9/tbJKBZUu5X6uz93J3tRu/kxWtcoQMnWzH61K6zSh9MPfuZ"
            + "liR4vhWCmjEULR7PELMxhQ+cUYlQc525xbMVkToByT3e3TdulHzu6LMlnx8tUv4wwS/o61GMavlt2/s6+uu/Xf3O17+PfMfc1WtjtBkQnI2rvZ/7dv5+/z97"
            + "MSKulAWp1+im9vfWvrgbqSiR09mi9KkQHX1PZNnau4qUJ6XlElHG5acobXY77bhjPuJZu7vbjCBK7pB0htQrgDIFZ7QlswhtoG28311yhvQFqXVHN0tHOst+"
            + "cF6N4nDmOxQiWko1wmk9O8q+5BRh+SFCG1bhWDuOs0k1Q2pptllFhP2J0IYVKPzMItWrgyTe9+OjCKzBeWZGZkgTL32M6LRmOn6sIdpNQStTkCYToSBI8LZm"
            + "HUBFoSiNoyABEMLyBclosw0jdzIRrzEsX5BmFG3JLEIbZmEAtU7erEhB+o8REsBYSxcko6C2jDiBEksXpCezIzKwXDfPIOr53QbFiR+MrUkgACP7mtei9Hyo"
            + "/bH4bfxmSDRl2a7tMWWOga8Z08IFydIH2ZjR5xtEXf3stvAdfksWJPoyoncsV7NXVJ6/uuP1z/Pfz3x+BQoSXbVItPfljtmSebb9mX2WdFSMPm1n7/dMbQue"
            + "9+XWoldYrou6jzXbVZKskY7BTOdzdu8xdvbYX/1crc9nZobEEHfW16+MWLOONhWjXGoUk8fCM6WuBWlvSWXWZRbqOlNUviV/5sJEfyWFYS+2as5stkXitstU"
            + "sPRgtronf4XRZvTp/pX2fYqfo8/fLV5RPPcjS3tndBRLz3Py7f9rbvcxeRw037kalb3WSVitIEXdx5KiVCvZMyZ3hnO5git9WIs+a4VYaLpjNaeZNUcbM5/Q"
            + "DKPqswWpdjKWFLdv2+7xdH3087iSM31Z6zh4N2NcVN+hK8sre0le+tmSNs14IrMZNepstRZveXkdI2Yt2yJFqdk1mhoHrlYhUZBiGnXrdoaitNKMnnO2BYpS"
            + "lxnS6NmN5I7r2yCmx5LYJ0cz9k8/X7NdM3U21LGdjMeMsdMs0aNc1JPcuWR743GLUauYpfVMP2qONXkOqebOes/TWqImSu3bzo8oRpzV4gaf0dIk/93nV7J1"
            + "dORSY6bkWierL+uFakxpgfm0vCO5We0daLAXU2dfFBsh3lK9y27vwO4dYIlNJCMfrGRtj7c4uvIQek/pgv7Ka4jatQb2lQ6KLC/TU433Qp792RJpO+usa6Ss"
            + "48zyiNk8Ga9/bo3eIpK6s57lBZrMy4ye2Qb4W8ObxabosHu8VwyuMpsni23wL77UeUNHK7z+hdy2gZdDJAEAh1znBAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
            + "AAAAAAAAAAAAAAAAAAAAAICfuv4AntjnDzG0q24AAAAASUVORK5CYII=");
}
