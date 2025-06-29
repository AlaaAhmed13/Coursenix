﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Coursenix.Models;
using Coursenix.ViewModels;
using Microsoft.AspNetCore.Authorization;

namespace Coursenix.Controllers
{
    [Authorize(Roles = "Teacher")]
    public class GroupsController : Controller
    {
        private readonly Context _context;

        public GroupsController(Context context)
        {
            _context = context;
        }

        public async Task<IActionResult> TakeAttendance(int id)
        {
            var group = await _context.Groups
                .Include(g => g.GradeLevel)
                .Include(g => g.Bookings)
                .ThenInclude(b => b.Student)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group == null)
            {
                return NotFound();
            }

            var students = group.Bookings?.Select(b => b.Student).ToList() ?? new List<Student>();

            var viewModel = new TakeAttendanceViewModel
            {
                GroupId = group.Id,
                GroupName = group.Name ?? $"Group {group.Id}",
                //CourseName = group.GradeLevel.Course.Name,
                Grade = group.GradeLevel?.NumberOfGrade ?? 0,
                Days = string.Join(" & ", group.SelectedDays),
                StartTime = group.StartTime.ToString("h:mm tt"),
                EndTime = group.EndTime.ToString("h:mm tt"),
                SessionDate = DateTime.Now.Date,
                Students = students.Select(s => new StudentAttendanceItem
                {
                    StudentId = s.Id,
                    StudentName = s.Name,
                    IsPresent = false
                }).ToList()
            };

            return View(viewModel);
        }

        // POST: TakeAttendance
        [HttpPost]
        public async Task<IActionResult> TakeAttendance(TakeAttendanceViewModel model)
        {
            try
            {
                var session = new Session
                {
                    GroupId = model.GroupId,
                    SessionDateTime = model.SessionDate
                };

                _context.Sessions.Add(session);
                await _context.SaveChangesAsync();

                var attendanceRecords = model.Students.Select(s => new Attendance
                {
                    StudentId = s.StudentId,
                    SessionId = session.Id,
                    IsPresent = s.IsPresent
                }).ToList();

                _context.Attendances.AddRange(attendanceRecords);
                await _context.SaveChangesAsync();

                var successViewModel = new AttendanceSuccessViewModel
                {
                    GroupId = model.GroupId,
                    GroupName = model.GroupName,
                    CourseName = model.CourseName,
                    Grade = model.Grade,
                    Days = model.Days,
                    StartTime = model.StartTime,
                    EndTime = model.EndTime,
                    SessionDate = model.SessionDate,
                    PresentStudents = model.Students.Where(s => s.IsPresent).Select(s => s.StudentName).ToList(),
                    AbsentStudents = model.Students.Where(s => !s.IsPresent).Select(s => s.StudentName).ToList()
                };

                return View("AttendanceSuccess", successViewModel);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "An error occurred while saving attendance.");
                return View(model);
            }
        }

        // GET: StudentAttendance
        public async Task<IActionResult> StudentAttendance(int id)
        {
            var group = await _context.Groups
                .Include(g => g.GradeLevel)
                .Include(g => g.Bookings)
                .ThenInclude(b => b.Student)
                .ThenInclude(s => s.Attendances)
                .ThenInclude(a => a.Session)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group == null)
            {
                return NotFound();
            }

            var students = group.Bookings?.Select(b => b.Student).ToList() ?? new List<Student>();

            var viewModel = new StudentAttendanceViewModel
            {
                GroupId = group.Id,
                GroupName = group.Name ?? $"Group {group.Id}",
                //CourseName = group.GradeLevel?.Subject ?? "N/A",
                Grade = group.GradeLevel?.NumberOfGrade ?? 0,
                Days = string.Join(" & ", group.SelectedDays),
                CourseId = group.GradeLevel.CourseId,
                StartTime = group.StartTime.ToString("h:mm tt"),
                EndTime = group.EndTime.ToString("h:mm tt"),
                Students = students.Select(s => new StudentAttendanceRecord
                {
                    StudentId = s.Id,
                    StudentName = s.Name,
                    PhoneNumber = s.PhoneNumber ?? "N/A",
                    ParentPhoneNumber = s.ParentPhoneNumber ?? "N/A",
                    AbsencePercentage = CalculateAbsencePercentage(s, group.Id)
                }).ToList()
            };

            return View(viewModel);
        }

        private double CalculateAbsencePercentage(Student student, int id)
        {
            var totalSessions = _context.Sessions.Count(s => s.GroupId == id);

            if (totalSessions == 0) return 100.0;

            var studentAttendances = student.Attendances?
                .Where(a => a.Session.GroupId == id)
                .ToList() ?? new List<Attendance>();

            if (!studentAttendances.Any()) return 100.0;

            var absentSessions = studentAttendances.Count(a => !a.IsPresent);

            return Math.Round((double)absentSessions / totalSessions * 100, 1);
        }
        // POST: DeleteStudent (from group)
        [HttpPost]
        public async Task<IActionResult> DeleteStudent(int studentId, int id)
        {
            try
            {
                var booking = await _context.Bookings
                    .FirstOrDefaultAsync(b => b.StudentId == studentId && b.GroupId == id);

                if (booking != null)
                {
                    _context.Bookings.Remove(booking);
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Student removed from group successfully!";
                }

                return RedirectToAction("StudentAttendance", new { id });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while removing the student.";
                return RedirectToAction("StudentAttendance", new { id });
            }
        }

        [HttpGet]
        public async Task<IActionResult> SearchStudents(int id, string searchTerm)
        {
            var group = await _context.Groups
                .Include(g => g.Bookings)
                .ThenInclude(b => b.Student)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group == null)
            {
                return Json(new List<object>());
            }

            var students = group.Bookings?
                .Select(b => b.Student)
                .Where(s => string.IsNullOrEmpty(searchTerm) ||
                           s.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Select(s => new
                {
                    id = s.Id,
                    name = s.Name,
                    phoneNumber = s.PhoneNumber ?? "N/A",
                    parentPhoneNumber = s.ParentPhoneNumber ?? "N/A"
                })
                .ToList();

            return Json(students);
        }
    }
}