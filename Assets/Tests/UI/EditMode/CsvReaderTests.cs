using System;
using System.Linq;
using DailyFranchise.App.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    public class CsvReaderTests
    {
        [Test]
        public void 헤더_이름으로_값을_꺼내고_따옴표_없는_칸은_앞뒤_공백을_자른다()
        {
            var rows = CsvReader.Parse("id, name ,avg\nA1,  하윤재 ,.301\nA2,도경민,.279\n");
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0]["name"], Is.EqualTo("하윤재"));
            Assert.That(rows[0]["NAME"], Is.EqualTo("하윤재"), "열 이름은 대소문자 무시");
            Assert.That(rows[1].Double("avg"), Is.EqualTo(0.279).Within(1e-9));
            Assert.That(rows[1].Line, Is.EqualTo(3));
        }

        [Test]
        public void 따옴표_칸은_쉼표_줄바꿈_이스케이프_따옴표를_담는다()
        {
            var rows = CsvReader.Parse("a,b,c\n\"1,2\",\"say \"\"hi\"\"\",\"line1\nline2\"\nx,y,z");
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0]["a"], Is.EqualTo("1,2"));
            Assert.That(rows[0]["b"], Is.EqualTo("say \"hi\""));
            Assert.That(rows[0]["c"], Is.EqualTo("line1\nline2"));
            Assert.That(rows[1]["c"], Is.EqualTo("z"), "마지막 줄바꿈 없어도 읽는다");
            Assert.That(rows[1].Line, Is.EqualTo(4), "따옴표 안 줄바꿈도 행 번호에 센다");
        }

        [Test]
        public void BOM_CRLF_빈_행_주석_행을_처리한다()
        {
            var rows = CsvReader.Parse("﻿# 주석\r\nid,n\r\n\r\n1,가\r\n# 중간 주석\r\n2,나\r\n");
            Assert.That(rows.Select(r => r["id"]), Is.EqualTo(new[] { "1", "2" }));
            Assert.That(rows[1]["n"], Is.EqualTo("나"));
        }

        [Test]
        public void 빈_칸은_빈_문자열이고_숫자_읽기는_0_선택값은_null()
        {
            var row = CsvReader.Parse("a,b,c\n,,x").Single();
            Assert.That(row["a"], Is.EqualTo(""));
            Assert.That(row.Int("a"), Is.EqualTo(0));
            Assert.That(row.Double("b"), Is.EqualTo(0));
            Assert.That(row.Optional("a"), Is.Null);
            Assert.That(row.Optional("없는열"), Is.Null);
            Assert.Throws<FormatException>(() => row.Required("a"));
            Assert.Throws<FormatException>(() => _ = row["없는열"]);
            Assert.Throws<FormatException>(() => row.Int("c"));
            Assert.Throws<FormatException>(() => row.Double("c"));
        }

        [TestCase("a,b\n1,2,3", "2행")]
        [TestCase("a,b\n1", "2행")]
        [TestCase("a,b\n\"1,2", "따옴표")]
        [TestCase("a,a\n1,2", "중복")]
        [TestCase("a,,c\n1,2,3", "빈 열")]
        [TestCase("# 주석만\n\n", "헤더")]
        [TestCase("a,b\n\"1\"x,2", "따옴표")]
        public void 형식이_깨진_CSV는_행_번호가_담긴_FormatException(string csv, string message)
        {
            var e = Assert.Throws<FormatException>(() => CsvReader.Parse(csv));
            Assert.That(e.Message, Does.Contain(message));
        }
    }
}
