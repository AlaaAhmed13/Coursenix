﻿using System.Security.Claims;
using Coursenix.Models;
using Coursenix.Models.ViewModels;
using Coursenix.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Coursenix.Controllers
{
    public class StudentController : Controller
    {
        //private readonly Context _context = new Context()

        // all view of student 
        private readonly Context _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        public StudentController(UserManager<AppUser> userManager, Context context, SignInManager<AppUser> signInManager)
        {
            _userManager = userManager;
            _context = context;
            _signInManager = signInManager;
        }

        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var userId = _userManager.GetUserId(User);

            var student = await _context.Students
                .Include(s => s.AppUser)
                .FirstOrDefaultAsync(s => s.AppUserId == userId);

            if (student == null) return NotFound();

            var vm = new StudentSettingsVM
            {
                fullName = student.Name,
                email = student.Email,
                phone = student.PhoneNumber,
                parentPhone = student.ParentPhoneNumber,
                gradeLevel = student.Grade
            };

            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> Settings(StudentSettingsVM model)
        {
            var userId = _userManager.GetUserId(User);
            // curr user
            var user = await _userManager.GetUserAsync(User);

            var student = await _context.Students
                .Include(s => s.AppUser)
                .FirstOrDefaultAsync(s => s.AppUserId == userId);

            if (student == null) return NotFound();
            if (string.IsNullOrEmpty(model.CurrPassword))
            {
                ModelState.AddModelError(nameof(model.CurrPassword), "Please enter your current password");
                return View(model);
            }
            var result = await _signInManager.CheckPasswordSignInAsync(user, model.CurrPassword, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(nameof(model.CurrPassword), "Current password is incorrect.");
                return View(model);
            }
            if (!string.IsNullOrWhiteSpace(model.phone))
                student.PhoneNumber = model.phone;

            if (!string.IsNullOrWhiteSpace(model.parentPhone))
                student.ParentPhoneNumber = model.parentPhone;

            if (model.gradeLevel.HasValue)
                student.Grade = model.gradeLevel.Value;

            await _userManager.UpdateAsync(student.AppUser);
            _context.Update(student);
            await _context.SaveChangesAsync();

            // Password change 

            if (!string.IsNullOrEmpty(model.password))
            {

                var resultchange = await _userManager.ChangePasswordAsync(student.AppUser, model.CurrPassword, model.password);
                if (!resultchange.Succeeded)
                {
                    foreach (var error in resultchange.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);
                    return View(model);
                }

                // sgin in 
                await _signInManager.RefreshSignInAsync(student.AppUser);
            }
            return RedirectToAction("Index", "Course");
        }

        public async Task<IActionResult> DeleteStudents(int groupId, [FromBody] List<int> studentIds)
        {

            var group = await _context.Groups.FindAsync(groupId);
            if (group == null)
            {
                return NotFound($"Group with id {groupId} not found.");
            }

            foreach (var Id in studentIds)
            {
                var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.GroupId == groupId && b.StudentId == Id);
                if (booking == null)
                {
                    return NotFound($"Booking for student {Id} in group {groupId} not found.");
                }
                _context.Bookings.Remove(booking);

                // Update enrolled students count
                group.EnrolledStudentsCount = Math.Max(0, group.EnrolledStudentsCount - 1);

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpGet]
        // course id 
        public async Task<IActionResult> Enroll(int id)
        {
            var course = await _context.Courses
                .Include(c => c.Teacher)
                .Include(c => c.GradeLevels)
                    .ThenInclude(g => g.Groups)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course == null)
                return NotFound();

            var vm = new EnrollViewModel
            {
                CourseId = course.Id,
                CourseName = course.Name,
                Description = course.Description,
                Location = course.Location,
                TeacherName = course.Teacher.Name,
                TeacherEmail = course.Teacher.Email,
                GradeLevels = course.GradeLevels.Select(g => new GradeLevelViewModel
                {
                    GradeLevelId = g.Id,
                    NumberOfGrade = g.NumberOfGrade,
                    Price = g.Price,
                    NumberOfClassesPerWeek = g.Groups
                                          .SelectMany(grp => grp.SelectedDays) // Flatten all SelectedDays lists
                                          .Distinct() 
                                          .Count(),    
                    Groups = g.Groups.Select(grp => new GroupsGradeLevel
                    {
                        GroupId = grp.Id,
                        Name = grp.Name,
                        SelectedDays = grp.SelectedDays,
                        StartTime = grp.StartTime,
                        EndTime = grp.EndTime,
                        TotalSeats = grp.TotalSeats,
                        EnrolledStudentsCount = grp.EnrolledStudentsCount
                    }).ToList()
                }).ToList()
            };

            return View(vm);
        }

        [HttpPost]
        [ActionName("Enroll")] 
        public async Task<IActionResult> EnrollConfirmed(int groupId)
        {
            var userId = _userManager.GetUserId(User);

            if (userId == null)
            {
                TempData["ErrorMessage"] = "You must be logged in to enroll in a course.";
                return RedirectToAction("Login", "Account"); // Redirect to login page (adjust if your login action is different)
            }

            var student = await _context.Students
                .Include(s => s.AppUser)
                .FirstOrDefaultAsync(s => s.AppUserId == userId);

            if (student == null)
            {
                TempData["ErrorMessage"] = "Student profile not found. Please complete your profile.";
                return RedirectToAction("StudentRegister", "Account"); // Example: Redirect to a profile creation page
            }

            var group = await _context.Groups
               .Include(g => g.Bookings) 
               .Include(g => g.GradeLevel) 
                   .ThenInclude(gl => gl.Course)
               .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group == null)
            {
                TempData["ErrorMessage"] = "Selected group not found.";
                return RedirectToAction("Index", "Home"); 
            }

            if (group.Bookings.Any(b => b.StudentId == student.Id))
            {
                TempData["ErrorMessage"] = "You are already enrolled in this group.";
                return RedirectToAction("Enroll", new { id = group.GradeLevel.CourseId });
            }

            if (group.EnrolledStudentsCount >= group.TotalSeats)
            {
                TempData["ErrorMessage"] = "No available seats in this group.";
                return RedirectToAction("Enroll", new { id = group.GradeLevel.CourseId });
            }

            var booking = new Booking
            {
                StudentId = student.Id,
                GroupId = groupId,
                BookingDate = DateTime.UtcNow
            };

            group.Bookings.Add(booking);

            group.EnrolledStudentsCount++;

            try
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Enrollment successful!";
                return RedirectToAction("Index", "Home"); 
            }
            catch (DbUpdateException ex)
            {
                TempData["ErrorMessage"] = "An unexpected error occurred during enrollment. Please try again.";
                return RedirectToAction("Enroll", new { id = group.GradeLevel.CourseId });
            }
        }

        public async Task<IActionResult> Dashboard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account"); 
            }

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.AppUserId == userId);
            if (student == null)
            {
                return NotFound("Student record not found.");
            }

            var bookings = await _context.Bookings
                .Where(b => b.StudentId == student.Id)
                .Include(b => b.Group)
                    .ThenInclude(g => g.GradeLevel)
                        .ThenInclude(gl => gl.Course)
                            .ThenInclude(c => c.Teacher)
                .ToListAsync();

            var viewModel = new StudentDashboardViewModel
            {
                StudentName = student.Name,
                Courses = new List<StudentDashboardViewModel.CourseInfo>()
            };

            foreach (var booking in bookings)
            {
                var group = booking.Group;
                var course = group.GradeLevel.Course;
                var teacher = course.Teacher;

                var sessions = await _context.Sessions
                    .Where(s => s.GroupId == group.Id)
                    .ToListAsync();

                var attendances = await _context.Attendances
                    .Where(a => a.StudentId == student.Id && a.Session.GroupId == group.Id)
                    .ToListAsync();

                var totalSessions = sessions.Count;
                var absences = attendances.Count(a => !a.IsPresent);
                var absenceRatio = totalSessions > 0 ? (absences * 100.0 / totalSessions).ToString("F0") + "%" : "0%";
                var absenceValue = totalSessions > 0 ? absences * 100.0 / totalSessions : 0;
                var absenceClass = absenceValue <= 5 ? "low" : absenceValue <= 15 ? "medium" : "high";

                viewModel.Courses.Add(new StudentDashboardViewModel.CourseInfo
                {
                    CourseName = course.Name,
                    TeacherName = teacher.Name,
                    GroupName = group.Name ?? $"Group {group.Id}",
                    Location = group.Location ?? course.Location ?? "Not specified",
                    Days = group.SelectedDays,
                    TimeRange = $"{group.StartTime:HH:mm} - {group.EndTime:HH:mm}",
                    AbsenceRatio = absenceRatio,
                    AbsenceClass = absenceClass
                });
            }

            return View(viewModel);
        }

    }

}